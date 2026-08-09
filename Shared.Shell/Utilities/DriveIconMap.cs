using Shared.Shell.Models;

namespace Shared.Shell.Utilities
{
    public static class DriveIconMap
    {
        public static int GetImageresIconIndex(DriveVisualKind kind)
        {
            return kind switch
            {
                DriveVisualKind.SystemBitLockerProtectionOff => 214,
                DriveVisualKind.SystemBitLockerUnlocked => 213,
                DriveVisualKind.System => 31,
                DriveVisualKind.BitLockerLocked => 211,
                DriveVisualKind.BitLockerStatusUnknown => 70,
                DriveVisualKind.BitLockerUnlocked => 210,
                DriveVisualKind.BitLockerProtectionOff => 212,
                DriveVisualKind.Network => 28,
                DriveVisualKind.Removable => 30,
                DriveVisualKind.Optical => 25,
                _ => 30
            };
        }
    }
}
