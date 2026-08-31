namespace Shared.Shell.Utilities
{
    // Shared imageres.dll icon assignments for shell-owned windows.
    // These are fixed application identities, not per-folder/per-drive imagery.
    public static class ShellOwnedWindowIcons
    {
        public const string IconDllName = "imageres.dll";

        public const int TaskbarIconIndex = 249;
        public const int FileManagerIconIndex = 265;
        public const int BitLockerManagerIconIndex = 321;
        public const int ImagingManagerIconIndex = 30;
        public const int BitLockerUnlockIconIndex = 225;

        public static Icon? CreateWindowIcon(int iconIndex, int size = 32)
        {
            string iconPath = Path.Combine(Environment.SystemDirectory, IconDllName);
            return IconUtil.FromFileIconIndexIcon(iconPath, iconIndex, size);
        }

        public static Image? FromTaskbarIcon(int iconIndex, int size)
        {
            return Icons.FromTaskbarSystemDll(IconDllName, iconIndex, size);
        }

        public static bool TryGetIconIndexForExe(string? exePath, out int iconIndex)
        {
            iconIndex = 0;

            if (string.IsNullOrWhiteSpace(exePath))
                return false;

            string fileName;
            try
            {
                fileName = Path.GetFileName(exePath);
            }
            catch
            {
                return false;
            }

            if (fileName.Equals("FileManager.exe", StringComparison.OrdinalIgnoreCase))
            {
                iconIndex = FileManagerIconIndex;
                return true;
            }

            if (fileName.Equals("BitLocker.Manager.exe", StringComparison.OrdinalIgnoreCase))
            {
                iconIndex = BitLockerManagerIconIndex;
                return true;
            }

            if (fileName.Equals("Imaging.Manager.exe", StringComparison.OrdinalIgnoreCase))
            {
                iconIndex = ImagingManagerIconIndex;
                return true;
            }

            if (fileName.Equals("BitLocker.Unlock.exe", StringComparison.OrdinalIgnoreCase))
            {
                iconIndex = BitLockerUnlockIconIndex;
                return true;
            }

            return false;
        }
    }
}
