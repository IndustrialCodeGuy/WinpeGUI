using BitLocker.Core;
using Shared.Shell.Theming;
using System.Diagnostics;

namespace BitLocker.Manager;

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
                "BitLocker Manager must be run as administrator.",
                "BitLocker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return 1;
        }

        using Mutex? singleInstanceMutex = BitLockerManagerActivation.TryAcquireManagerMutex();
        if (singleInstanceMutex == null)
        {
            BitLockerManagerActivation.SignalExistingManager();
            return 0;
        }
       
        using EventWaitHandle activateEvent = BitLockerManagerActivation.CreateManagerActivateEvent();

        BitLockerLaunchArgs launchArgs = BitLockerLaunchArgs.Parse(args);
        using MainForm mainForm = new(launchArgs);
        mainForm.StartActivationListener(activateEvent);
        Application.Run(mainForm);
        return 0;
    }
}
