using BitLocker.Core;
using System.Diagnostics;
using Shared.Shell.Theming;

namespace BitLocker.Manager;

public partial class MainForm
{
    // The unlock helper is a separate elevated process so the manager can stay
    // responsive while the user enters credentials.
    private async Task LaunchUnlockWindowAsync(BitLockerVolumeInfo volume)
    {
        if (volume.IsLocked != true)
            return;

        string unlockPath = Path.Combine(AppContext.BaseDirectory, "BitLocker.Unlock.exe");
        if (!File.Exists(unlockPath))
        {
            ShowOperationError("Unlock Drive", $"BitLocker unlock helper was not found:\n{unlockPath}");
            return;
        }

        _btnUnlock.Enabled = false;
        _lblStatus.Text = "Unlock window opened.";
        _lblStatus.Visible = true;

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = unlockPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            };

            startInfo.ArgumentList.Add("--drive");
            startInfo.ArgumentList.Add(volume.MountPoint);

            if (ShellTheme.DarkMode)
                startInfo.ArgumentList.Add("--dark");

            Process? process = Process.Start(startInfo);
            if (process == null)
            {
                ShowOperationError("Unlock Drive", "Unable to start BitLocker unlock helper.");
                UpdateSelectedVolumePanel();
                return;
            }

            using (process)
            {
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                    RefreshSelectedVolumePanel();
                else
                    UpdateSelectedVolumePanel();
            }
        }
        catch (Exception ex)
        {
            ShowOperationError("Unlock Drive", ex.Message);
            UpdateSelectedVolumePanel();
        }
    }
}
