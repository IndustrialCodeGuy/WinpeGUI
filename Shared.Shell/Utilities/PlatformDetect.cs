using Microsoft.Win32;

namespace Shared.Shell.Utilities
{
    public static class PlatformDetect
    {
        // Evaluate once, cache forever (startup cost: a couple registry opens)
        public static readonly bool IsWinPE = ComputeIsWinPE();

        private static bool ComputeIsWinPE()
        {
            // 1) Strong signal: MiniNT
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT");
                if (k != null) return true;
            }
            catch { }

            // 2) Strong-ish signal: CurrentVersion\WinPE
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WinPE");
                if (k != null) return true;
            }
            catch { }

            // 3) Heuristic fallback: X:\Windows
            try
            {
                var sysRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "";
                if (sysRoot.StartsWith(@"X:\Windows", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            return false;
        }
    }
}