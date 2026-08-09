namespace Shared.Shell.Utilities
{
    // Shared image cache for shell chrome. Images returned from this class are
    // owned by the cache and may be assigned to multiple controls; callers should
    // detach them before disposing UI surfaces and should not dispose them directly.

    public static class Icons
    {
        private static readonly object _lock = new object();

        // Start menu caches.
        private static readonly Dictionary<string, Image> _startSystemCache =
            new(StringComparer.OrdinalIgnoreCase);

        // Fixed-path start menu icons, keyed by full path + size.
        private static readonly Dictionary<string, Image> _startPathCache =
            new(StringComparer.OrdinalIgnoreCase);

        // Taskbar EXE icons. Keyed by exe path + size.
        private static readonly Dictionary<string, Image> _taskbarExeCache =
            new(StringComparer.OrdinalIgnoreCase);

        // Fixed taskbar icons sourced from system DLL resources. This is separate
        // from the Start menu system cache so rebuilding the Start menu cannot
        // dispose images still assigned to live taskbar buttons.
        private static readonly Dictionary<string, Image> _taskbarSystemCache =
            new(StringComparer.OrdinalIgnoreCase);

        private static string CanonKeyPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path ?? "";
            try
            {
                return Path.IsPathRooted(path) ? Path.GetFullPath(path) : path;
            }
            catch
            {
                return path;
            }
        }

        // Start menu system DLL icon (imageres.dll, shell32.dll, etc.)
        public static Image? FromSystemDll(string dllName, int index, int size)
        {
            if (string.IsNullOrWhiteSpace(dllName) || size <= 0) return null;

            string full = Path.IsPathRooted(dllName)
                ? dllName
                : Path.Combine(Environment.SystemDirectory, dllName);

            full = CanonKeyPath(full);
            string key = $"sys|{full}|{index}|{size}";

            lock (_lock)
            {
                if (_startSystemCache.TryGetValue(key, out var cached))
                    return cached;
            }

            var img = IconUtil.FromFileIconIndex(full, index, size);
            if (img == null) return null;

            lock (_lock)
            {
                if (_startSystemCache.TryGetValue(key, out var existing))
                {
                    try { img.Dispose(); } catch { }
                    return existing;
                }

                _startSystemCache[key] = img;
                return img;
            }
        }

        // Start menu fixed-path icon (optional use; caches by *path* + size)
        // Good for: notepad.exe, cmd.exe, powershell.exe menu items, etc.
        public static Image? FromStartPath(string path, int size)
        {
            if (string.IsNullOrWhiteSpace(path) || size <= 0) return null;

            path = CanonKeyPath(path);

            string key = $"start|{path}|{size}";

            lock (_lock)
            {
                if (_startPathCache.TryGetValue(key, out var cached))
                    return cached;
            }

            Image? img = null;

            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            if (ext == ".exe" || ext == ".dll" || ext == ".ico")
                img = IconUtil.FromFileIconIndex(path, 0, size);

            if (img == null)
                img = IconUtil.FromFileAssociation(path, size);

            if (img == null) return null;

            lock (_lock)
            {
                if (_startPathCache.TryGetValue(key, out var existing))
                {
                    try { img.Dispose(); } catch { }
                    return existing;
                }

                _startPathCache[key] = img;
                return img;
            }
        }

        // Taskbar EXE icon (separate cache from Start menu; keyed by exe + size)
        // Uses embedded EXE icon first; association icon second.
        public static Image? FromTaskbarExe(string exePath, int size)
        {
            if (string.IsNullOrWhiteSpace(exePath) || size <= 0) return null;

            exePath = CanonKeyPath(exePath);

            string key = $"tbexe|{exePath}|{size}";

            lock (_lock)
            {
                if (_taskbarExeCache.TryGetValue(key, out var cached))
                    return cached;
            }

            Image? img = IconUtil.FromFileIconIndex(exePath, 0, size);
            if (img == null)
                img = IconUtil.FromFileAssociation(exePath, size);

            if (img == null) return null;

            lock (_lock)
            {
                if (_taskbarExeCache.TryGetValue(key, out var existing))
                {
                    try { img.Dispose(); } catch { }
                    return existing;
                }

                _taskbarExeCache[key] = img;
                return img;
            }
        }

        // Fixed taskbar system DLL icon (imageres.dll, shell32.dll, etc.).
        // Use this for shell-owned taskbar chrome/buttons that should not follow
        // the host process EXE icon or the window title-bar icon.
        public static Image? FromTaskbarSystemDll(string dllName, int index, int size)
        {
            if (string.IsNullOrWhiteSpace(dllName) || size <= 0) return null;

            string full = Path.IsPathRooted(dllName)
                ? dllName
                : Path.Combine(Environment.SystemDirectory, dllName);

            full = CanonKeyPath(full);
            string key = $"tbsys|{full}|{index}|{size}";

            lock (_lock)
            {
                if (_taskbarSystemCache.TryGetValue(key, out var cached))
                    return cached;
            }

            var img = IconUtil.FromFileIconIndex(full, index, size);
            if (img == null) return null;

            lock (_lock)
            {
                if (_taskbarSystemCache.TryGetValue(key, out var existing))
                {
                    try { img.Dispose(); } catch { }
                    return existing;
                }

                _taskbarSystemCache[key] = img;
                return img;
            }
        }

        // Call this when the taskbar icon size changes (so old-size bitmaps don’t linger)
        public static void ClearTaskbarCache()
        {
            List<Image> dispose = new List<Image>();

            lock (_lock)
            {
                foreach (var img in _taskbarExeCache.Values)
                    dispose.Add(img);

                foreach (var img in _taskbarSystemCache.Values)
                    dispose.Add(img);

                _taskbarExeCache.Clear();
                _taskbarSystemCache.Clear();
            }

            foreach (var img in dispose)
                try { img?.Dispose(); } catch { }
        }

        // Optional: call if you explicitly want to free Start menu caches
        public static void ClearStartCaches()
        {
            List<Image> dispose = new List<Image>();

            lock (_lock)
            {
                foreach (var img in _startSystemCache.Values) dispose.Add(img);
                foreach (var img in _startPathCache.Values) dispose.Add(img);

                _startSystemCache.Clear();
                _startPathCache.Clear();
            }

            foreach (var img in dispose)
                try { img?.Dispose(); } catch { }
        }
    }
}
