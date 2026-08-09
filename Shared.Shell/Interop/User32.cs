using System.Runtime.InteropServices;
using System.Text;

namespace Shared.Shell.Interop
{
    // =====================================================================
    //  USER32 INTEROP (Win32 P/Invoke surface)
    // =====================================================================
    //
    // Purpose:
    // - Centralizes the Win32 user32.dll calls and constants used by the shell.
    // - Keeps raw P/Invoke out of higher-level code (ShellForm / TaskWindowEnumerator / IconUtil).
    //
    // Who uses this file:
    // - TaskWindowEnumerator:
    //   - EnumWindows / IsWindowVisible / GetWindowText / GetClassName / GetWindowLong / GetWindow / GetWindowRect
    //     to build the task list and filter out non-task windows.
    //   - IsIconic / IsZoomed / ShowWindow / SetForegroundWindow / PostMessage to implement minimize/maximize/restore/close.
    //   - GetWindowThreadProcessId to map hwnd -> pid (then kernel32 process path lookup).
    //   - WM_GETICON + SendMessageTimeout + GetClassLongPtr(GCLP_HICON/HICONSM) to fetch fallback HICONs.
    // - IconUtil:
    //   - CopyIcon / DestroyIcon for safe HICON ownership when converting icons to bitmaps.
    //
    // Notes / Debug tips:
    // - SendMessageTimeout is used with SMTO_ABORTIFHUNG and a short timeout to avoid hanging on bad windows.
    // - GetClassLongPtr wrapper handles 32-bit vs 64-bit correctly (WinPE can be either depending on build).
    // - If a window icon looks wrong or missing, check:
    //     1) WM_GETICON path (SendMessageTimeout result)
    //     2) class icon path (GetClassLongPtr)
    //     3) calling code’s icon size (_taskbarIconPx) and cache invalidation logic.
    // =====================================================================

    public static class User32
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;

        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public static readonly IntPtr HWND_BOTTOM = new(1);

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const uint SPI_GETWORKAREA = 0x0030;
        public const uint SPI_SETWORKAREA = 0x002F;
        public const uint SPI_GETDESKWALLPAPER = 0x0073;
        public const uint SPIF_SENDCHANGE = 0x0002;

        public const uint GW_OWNER = 4;

        public const int SW_MAXIMIZE = 3;
        public const int SW_MINIMIZE = 6;
        public const int SW_RESTORE = 9;

        public const uint ASFW_ANY = 0xFFFFFFFF;

        public const uint WM_CLOSE = 0x0010;
        public const int WM_PAINT = 0x000F;
        public const int WM_NCPAINT = 0x0085;
        public const int LVM_FIRST = 0x1000;
        public const int LVM_GETHEADER = LVM_FIRST + 31;

        public const int WM_GETICON = 0x007F;
        public const int ICON_SMALL = 0;
        public const int ICON_BIG = 1;
        public const int ICON_SMALL2 = 2;

        public const int GCLP_HICON = -14;
        public const int GCLP_HICONSM = -34;

        // SendMessageTimeout flags
        public const uint SMTO_ABORTIFHUNG = 0x0002;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowPlacement(
            IntPtr hWnd,
            ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPlacement(
            IntPtr hWnd,
            ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool AllowSetForegroundWindow(uint dwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SystemParametersInfo(
            uint uiAction,
            uint uiParam,
            StringBuilder pvParam,
            uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(
            uint uiAction,
            uint uiParam,
            ref RECT pvParam,
            uint fWinIni);


        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        public static extern bool IsWindow_Native(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            int Msg,
            IntPtr wParam,
            IntPtr lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult
        );

        // Icon handle safety
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CopyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int TrackPopupMenuEx(
            IntPtr hmenu,
            uint fuFlags,
            int x,
            int y,
            IntPtr hwnd,
            IntPtr lptpm);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool InsertMenuW(
            IntPtr hMenu,
            uint uPosition,
            uint uFlags,
            uint uIDNewItem,
            string? lpNewItem);

        public static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetClassLongPtr64(hWnd, nIndex);
            return new IntPtr((int)GetClassLong32(hWnd, nIndex));
        }

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr", SetLastError = true)]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLong", SetLastError = true)]
        private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);
    }
}
