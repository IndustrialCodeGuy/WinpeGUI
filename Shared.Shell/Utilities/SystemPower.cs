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
            string systemDirectory = Environment.SystemDirectory;
            string wpeutilPath = Path.Combine(systemDirectory, "wpeutil.exe");

            if (!File.Exists(wpeutilPath))
            {
                throw new FileNotFoundException(
                    "wpeutil.exe was not found under the active Windows system directory.",
                    wpeutilPath);
            }

            return new ProcessStartInfo
            {
                FileName = wpeutilPath,
                Arguments = reboot ? "reboot" : "shutdown",
                WorkingDirectory = systemDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
    }
}
