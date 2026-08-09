using System.Runtime.InteropServices;
using BitLocker.Core;
using Shared.Shell.Theming;

namespace BitLocker.Unlock;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        ShellTheme.ConfigureFromArgs(args);

        if (!AdministratorCheck.IsRunningAsAdministrator())
        {
            MessageBox.Show(
                "BitLocker Unlock must be run as administrator.",
                "Unlock Drive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return 1;
        }

        BitLockerLaunchArgs launchArgs = BitLockerLaunchArgs.Parse(args);
        string? drivePath = launchArgs.DrivePath;

        if (string.IsNullOrWhiteSpace(drivePath))
        {
            MessageBox.Show(
                "A drive must be specified.",
                "Unlock Drive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return 1;
        }

        // Keep one unlock prompt per drive. A repeated launch signals the
        // existing window and exits without changing the unlock result.
        using Mutex? unlockMutex = BitLockerUnlockActivation.TryAcquireUnlockMutex(drivePath);
        if (unlockMutex == null)
        {
            try
            {
                AllowSetForegroundWindow(ASFW_ANY);
            }
            catch
            {
            }

            BitLockerUnlockActivation.SignalExistingUnlockWindow(drivePath);
            return 2;
        }

        using UnlockForm form = new(drivePath);
        Application.Run(form);
        return form.Unlocked ? 0 : 3;
    }

    // Allow the already-running helper to come forward when this instance
    // exits after signaling it.
    private const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
