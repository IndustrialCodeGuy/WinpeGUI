namespace Shared.Shell.Utilities
{
    public static class DriveSystemDetector
    {
        public static bool IsSystemVisualDrive(string driveRoot)
        {
            if (PlatformDetect.IsWinPE)
                return ContainsOfflineWindowsInstall(driveRoot);

            return IsRunningSystemDrive(driveRoot);
        }

        public static bool ContainsOfflineWindowsInstall(string driveRoot)
        {
            try
            {
                string root = NormalizeDriveRoot(driveRoot);

                if (IsXDrive(root))
                    return false;

                string systemHive = Path.Combine(root, "Windows", "System32", "Config", "SYSTEM");
                return File.Exists(systemHive);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsXDrive(string driveRoot)
        {
            string root = NormalizeDriveRoot(driveRoot);
            return string.Equals(root, @"X:\", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsRunningSystemDrive(string driveRoot)
        {
            try
            {
                string root = NormalizeDriveRoot(driveRoot);
                string systemRoot = NormalizeDriveRoot(Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty);

                return string.Equals(
                    root.TrimEnd('\\'),
                    systemRoot.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeDriveRoot(string path)
        {
            string root = Path.GetPathRoot(path) ?? path;
            return root.TrimEnd('\\') + "\\";
        }
    }
}
