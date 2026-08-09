using Shared.Shell.Interop;
using Shared.Shell.Utilities;
using System.Runtime.InteropServices;
using System.Text;

namespace Shell.Taskbar.Shell
{
    // =====================================================================
    //  TASK WINDOW ENUMERATOR (task list + taskbar icon resolution)
    // =====================================================================
    //
    // Purpose:
    // - Enumerates “task windows” (Explorer-style top-level app windows) for the taskbar.
    // - Provides safe window actions (activate/minimize/maximize/close).
    // - Supplies taskbar button icons with a consistent sizing + caching strategy.
    //
    // How it relates to the ShellTaskbarForm split:
    // - UI/ShellTaskbarForm.Taskbar.cs:
    //     - Calls GetTaskWindows() on the refresh timer to build/update task buttons.
    //     - Calls GetForegroundWindowSafe() + GetWindowProcessId() to track focus.
    //     - Calls Activate/Minimize/Restore/Maximize/Close from button/menu actions.
    // - UI/ShellTaskbarForm.IconSizing.cs / Metrics.cs:
    //     - When taskbar icon size changes, ShellTaskbarForm calls SetTaskbarIconSize(px).
    //     - That updates the internal requested icon size and clears caches.
    //
    // Icon selection model (important for debugging wrong/fuzzy icons):
    // - Shell-owned override: fixed imageres.dll icons for shell-owned windows.
    // - Primary path for external apps: EXE icon
    //     - TryGetExePathForWindow(hwnd) -> QueryFullProcessImageName (WinPE-friendly)
    //     - Icons.FromTaskbarExe(exePath, _taskbarIconPx)
    //     - This is the preferred “Explorer-like” behavior (stable, high quality).
    // - Fallback path: Window/class icon handles
    //     - WM_GETICON + class icon (GCLP_HICON / GCLP_HICONSM)
    //     - Converted using IconUtil.FromWindowIconHandles(..., _taskbarIconPx)
    //     - Cached per hwnd in _windowIconFallbackCache.
    //     - IMPORTANT: this cache must be cleared when windows close (TryRemoveCachedIcon)
    //       and when size changes (SetTaskbarIconSize -> ClearAndDisposeWindowFallbackCache).
    //
    // Cache ownership rules:
    // - EXE icons are cached in Utilities/Icons (_taskbarExeCache); cleared via Icons.ClearTaskbarCache().
    // - Fallback window icons are cached HERE per hwnd and MUST be disposed here.
    // - ShellTaskbarForm should call TryRemoveCachedIcon(hwnd) when removing a task button to avoid leaks.
    //
    // Window enumeration filtering (debugging “missing” or “extra” task buttons):
    // - Requires: visible window + non-empty title + not toolwindow/noactivate + no owner + has rect > 1px.
    // - Requires: either WS_EX_APPWINDOW OR WS_OVERLAPPEDWINDOW.
    // - Excludes blocked classes (Shell_TrayWnd, etc.).
    // If an app doesn’t show up, check exstyle/style/class/title conditions.
    //
    // WinPE / restricted environment notes:
    // - Uses QueryFullProcessImageName via OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)
    //   because Process.MainModule can fail under restricted tokens.
    // - SendMessageTimeout used for WM_GETICON to avoid hung windows.
    //
    // Debug tips:
    // - “Icons don’t resize after DPI/layout change”:
    //     Verify ShellTaskbarForm calls SetTaskbarIconSize(px) AND clears/refreshes button Image references.
    // - “Icons leak / memory grows”:
    //     Ensure ShellTaskbarForm calls TryRemoveCachedIcon(hwnd) when a window disappears,
    //     and that size changes call ClearAndDisposeWindowFallbackCache.
    // - “Wrong icon for a window”:
    //     Check TryGetExePathForWindow outcome (pid/path validity), then fallback handle path.
    // =====================================================================

    internal static class TaskWindowEnumerator
    {
        // =====================================================================
        //  FIELDS / CACHES
        // =====================================================================

        #region Fields / Caches

        private static readonly Dictionary<IntPtr, Image> _windowIconFallbackCache = [];
        private static readonly Dictionary<IntPtr, (uint Pid, string? ExePath)> _windowExePathCache = [];

        private static readonly object _iconLock = new object();
        private static int _taskbarIconPx = 16;

        #endregion

        // =====================================================================
        //  PUBLIC: ICON API (taskbar icons)
        // =====================================================================

        #region Public - Icon API

        public static void SetTaskbarIconSize(int iconPx)
        {
            if (iconPx <= 0) iconPx = 16;
            if (_taskbarIconPx == iconPx) return;

            _taskbarIconPx = iconPx;
            ClearIconCaches();
        }

        public static void ClearIconCaches()
        {
            Icons.ClearTaskbarCache();
            ClearAndDisposeWindowFallbackCache();
        }

        public static Image? GetTaskbarIcon(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;

            uint pid = GetWindowProcessId(hwnd);

            // Cache the HWND -> PID/path lookup so the 400ms taskbar refresh does
            // not repeatedly OpenProcess/QueryFullProcessImageName for stable windows.
            string? exe = TryGetExePathForWindow(hwnd, pid);

            // Shell-owned Explorer windows should have a stable taskbar identity
            // independent of the dynamic title-bar/current-location icon. After
            // the taskbar split, file-manager windows belong to FileManager.exe
            // rather than the taskbar process itself.
            // Shell-owned windows should have stable taskbar identities
            // independent of their EXE icon or any dynamic title-bar icon.
            if (ShellOwnedWindowIcons.TryGetIconIndexForExe(exe, out int iconIndex))
            {
                Image? shellIcon = ShellOwnedWindowIcons.FromTaskbarIcon(iconIndex, _taskbarIconPx);

                if (shellIcon != null)
                    return shellIcon;
            }

            // 1) Prefer EXE icon for external applications.
            if (!string.IsNullOrWhiteSpace(exe))
            {
                var img = Icons.FromTaskbarExe(exe, _taskbarIconPx);
                if (img != null)
                    return img;
            }

            // 2) Fallback: window icon (WM_GETICON/class icon)
            lock (_iconLock)
            {
                if (_windowIconFallbackCache.TryGetValue(hwnd, out var cachedWnd))
                    return cachedWnd;
            }

            GetWindowIconHandles(hwnd, out var hSmall, out var hLarge);
            if (hSmall == IntPtr.Zero && hLarge == IntPtr.Zero)
                return null;

            var bmp = IconUtil.FromWindowIconHandles(hSmall, hLarge, _taskbarIconPx);
            if (bmp == null)
                return null;

            lock (_iconLock)
            {
                if (_windowIconFallbackCache.TryGetValue(hwnd, out var existing))
                {
                    try { bmp.Dispose(); } catch { }
                    return existing;
                }
                _windowIconFallbackCache[hwnd] = bmp;
                return bmp;
            }
        }

        // Called when a window goes away (so we don't keep hwnd fallback icons forever)
        public static bool TryRemoveCachedIcon(IntPtr hwnd)
        {
            Image? img = null;
            bool removedAnything = false;

            lock (_iconLock)
            {
                if (_windowIconFallbackCache.Remove(hwnd, out img))
                    removedAnything = true;

                if (_windowExePathCache.Remove(hwnd))
                    removedAnything = true;
            }

            try { img?.Dispose(); } catch { }
            return removedAnything;
        }

        #endregion

        // =====================================================================
        //  PRIVATE: ICON CACHE MAINTENANCE
        // =====================================================================

        #region Private - Icon Cache Maintenance

        private static void ClearAndDisposeWindowFallbackCache()
        {
            List<Image> dispose = new List<Image>();

            lock (_iconLock)
            {
                foreach (var img in _windowIconFallbackCache.Values) dispose.Add(img);
                _windowIconFallbackCache.Clear();
                _windowExePathCache.Clear();
            }

            foreach (var img in dispose)
                try { img?.Dispose(); } catch { }
        }

        #endregion

        // =====================================================================
        //  PRIVATE: WINDOW ICON HANDLE RETRIEVAL (fallback path)
        // =====================================================================

        #region Private - Window Icon Handle Retrieval

        // ---- Window icon handle retrieval (fallback path) ----
        // Return BOTH candidates so IconUtil can apply the same small/large pick logic everywhere.
        private static void GetWindowIconHandles(IntPtr hwnd, out IntPtr hSmall, out IntPtr hLarge)
        {
            hSmall = IntPtr.Zero;
            hLarge = IntPtr.Zero;

            if (hwnd == IntPtr.Zero)
                return;

            IntPtr TryGetIconViaMessage(int which)
            {
                IntPtr result;
                IntPtr r = User32.SendMessageTimeout(
                    hwnd,
                    User32.WM_GETICON,
                    new IntPtr(which),
                    IntPtr.Zero,
                    User32.SMTO_ABORTIFHUNG,
                    75,
                    out result);

                return (r == IntPtr.Zero) ? IntPtr.Zero : result;
            }

            // Small candidates
            hSmall = TryGetIconViaMessage(User32.ICON_SMALL2);
            if (hSmall == IntPtr.Zero) hSmall = TryGetIconViaMessage(User32.ICON_SMALL);
            if (hSmall == IntPtr.Zero) hSmall = User32.GetClassLongPtr(hwnd, User32.GCLP_HICONSM);

            // Large candidate
            hLarge = TryGetIconViaMessage(User32.ICON_BIG);
            if (hLarge == IntPtr.Zero) hLarge = TryGetIconViaMessage(User32.ICON_SMALL2); //optional
            if (hLarge == IntPtr.Zero) hLarge = TryGetIconViaMessage(User32.ICON_SMALL); //optional
            if (hLarge == IntPtr.Zero) hLarge = User32.GetClassLongPtr(hwnd, User32.GCLP_HICON);
        }

        #endregion

        // =====================================================================
        //  PRIVATE: EXE PATH RESOLUTION (primary path)
        // =====================================================================

        #region Private - EXE Path Resolution

        // ---- EXE path resolution (primary path) ----
        private static string? TryGetExePathForWindow(IntPtr hwnd, uint pid)
        {
            if (hwnd == IntPtr.Zero || pid == 0)
                return null;

            lock (_iconLock)
            {
                if (_windowExePathCache.TryGetValue(hwnd, out var cached))
                {
                    if (cached.Pid == pid)
                        return cached.ExePath;

                    _windowExePathCache.Remove(hwnd);
                }
            }

            string? exePath = null;

            try
            {
                // Try QueryFullProcessImageName first (more reliable than Process.MainModule in restricted contexts)
                string? p = TryGetProcessImagePath((int)pid);
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                    exePath = p;
            }
            catch { }

            lock (_iconLock)
            {
                if (_windowExePathCache.TryGetValue(hwnd, out var existing) && existing.Pid == pid)
                    return existing.ExePath;

                _windowExePathCache[hwnd] = (pid, exePath);
            }

            return exePath;
        }

        private static string? TryGetProcessImagePath(int pid)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return null;

                var sb = new StringBuilder(1024);
                int len = sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref len))
                    return null;

                return sb.ToString();
            }
            catch
            {
                return null;
            }
            finally
            {
                if (h != IntPtr.Zero) CloseHandle(h);
            }
        }

        #endregion

        // =====================================================================
        //  PUBLIC: WINDOW ENUMERATION + WINDOW ACTIONS
        // =====================================================================

        #region Public - Window Enumeration / Actions

        public static List<TaskWindow> GetTaskWindows(IReadOnlySet<IntPtr>? excludedWindows = null)
        {
            var list = new List<TaskWindow>();

            User32.EnumWindows((hwnd, lParam) =>
            {
                try
                {
                    if (excludedWindows != null && excludedWindows.Contains(hwnd))
                        return true;

                    if (!User32.IsWindowVisible(hwnd))
                        return true;

                    int exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);

                    if ((exStyle & User32.WS_EX_TOOLWINDOW) == User32.WS_EX_TOOLWINDOW)
                        return true;

                    if ((exStyle & User32.WS_EX_NOACTIVATE) == User32.WS_EX_NOACTIVATE)
                        return true;

                    IntPtr owner = User32.GetWindow(hwnd, User32.GW_OWNER);
                    if (owner != IntPtr.Zero)
                        return true;

                    if (!User32.GetWindowRect(hwnd, out User32.RECT rc))
                        return true;

                    int w = rc.Right - rc.Left;
                    int h = rc.Bottom - rc.Top;
                    if (w <= 1 || h <= 1)
                        return true;

                    int style = User32.GetWindowLong(hwnd, User32.GWL_STYLE);
                    bool isOverlapped = (style & User32.WS_OVERLAPPEDWINDOW) == User32.WS_OVERLAPPEDWINDOW;
                    bool isAppWindow = (exStyle & User32.WS_EX_APPWINDOW) == User32.WS_EX_APPWINDOW;

                    if (!isAppWindow && !isOverlapped)
                        return true;

                    string cls = GetClassNameString(hwnd);
                    if (IsBlockedClass(cls))
                        return true;

                    var title = GetWindowTitle(hwnd);
                    if (string.IsNullOrWhiteSpace(title))
                        return true;

                    list.Add(new TaskWindow { Hwnd = hwnd, Title = title, ClassName = cls });
                }
                catch { }

                return true;
            }, IntPtr.Zero);

            return list;
        }

        public static bool IsWindow(IntPtr hwnd) => User32.IsWindow_Native(hwnd);
        public static bool IsMinimized(IntPtr hwnd) => User32.IsIconic(hwnd);
        public static bool IsMaximized(IntPtr hwnd) => User32.IsZoomed(hwnd);

        public static void Restore(IntPtr hwnd) => User32.ShowWindow(hwnd, User32.SW_RESTORE);
        public static void Minimize(IntPtr hwnd) => User32.ShowWindow(hwnd, User32.SW_MINIMIZE);
        public static void Maximize(IntPtr hwnd) => User32.ShowWindow(hwnd, User32.SW_MAXIMIZE);

        public static void Activate(IntPtr hwnd) => User32.SetForegroundWindow(hwnd);
        public static void Close(IntPtr hwnd) => User32.PostMessage(hwnd, User32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

        public static IntPtr GetForegroundWindowSafe()
        {
            try { return User32.GetForegroundWindow(); }
            catch { return IntPtr.Zero; }
        }

        public static uint GetWindowProcessId(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return 0;
            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            return pid;
        }

        #endregion

        // =====================================================================
        //  PRIVATE: FILTERS + STRING HELPERS
        // =====================================================================

        #region Private - Filters / String Helpers

        private static bool IsBlockedClass(string cls)
        {
            if (string.IsNullOrWhiteSpace(cls)) return false;

            switch (cls)
            {
                case "Windows.UI.Core.CoreWindow":
                case "ApplicationFrameWindow":
                case "XamlExplorerHostIslandWindow":
                case "Shell_TrayWnd":
                case "DV2ControlHost":
                case "tooltips_class32":
                case "NotifyIconOverflowWindow":
                case "TaskListThumbnailWnd":
                    return true;
                default:
                    return false;
            }
        }

        private static string? GetWindowTitle(IntPtr hwnd)
        {
            var sb = new StringBuilder(512);
            int len = User32.GetWindowText(hwnd, sb, sb.Capacity);
            if (len <= 0) return null;
            return sb.ToString();
        }

        private static string GetClassNameString(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            int len = User32.GetClassName(hwnd, sb, sb.Capacity);
            if (len <= 0) return "";
            return sb.ToString();
        }

        #endregion

        // =====================================================================
        //  PINVOKE (kernel32) for process path
        // =====================================================================

        #region PInvoke - kernel32 (Process Path)

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        #endregion
    }
}
