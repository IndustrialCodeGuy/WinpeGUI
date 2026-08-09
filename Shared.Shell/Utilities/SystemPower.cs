using System.Diagnostics;

namespace Shared.Shell.Utilities
{
    public static class SystemPower
    {
        public static bool TryRequestSystemPowerAction(bool reboot, out string? error)
        {
            try
            {
                using Process? process = Process.Start(BuildSystemPowerStartInfo(reboot));
                if (process == null)
                {
                    error = "Process.Start returned null.";
                    return false;
                }

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public static ProcessStartInfo BuildSystemPowerStartInfo(bool reboot)
        {
            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);

            if (string.IsNullOrWhiteSpace(systemDirectory))
            {
                string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrWhiteSpace(windowsDirectory))
                    systemDirectory = Path.Combine(windowsDirectory, "System32");
            }

            if (string.IsNullOrWhiteSpace(systemDirectory))
                systemDirectory = @"X:\Windows\System32";

            string wpeutilPath = Path.Combine(systemDirectory, "wpeutil.exe");
            if (PlatformDetect.IsWinPE && File.Exists(wpeutilPath))
            {
                return new ProcessStartInfo
                {
                    FileName = wpeutilPath,
                    Arguments = reboot ? "reboot" : "shutdown",
                    WorkingDirectory = systemDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

            return new ProcessStartInfo
            {
                FileName = Path.Combine(systemDirectory, "shutdown.exe"),
                Arguments = reboot ? "/r /t 0" : "/s /t 0",
                WorkingDirectory = systemDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
    }
}
