using BitLocker.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

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
                "Imaging Manager must be run as administrator.",
                "Imaging Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        using Mutex? instanceMutex = ImagingManagerActivation.TryAcquireManagerMutex();
        if (instanceMutex == null)
        {
            ImagingManagerActivation.SignalExistingManager();
            return 0;
        }

        using EventWaitHandle activateEvent = ImagingManagerActivation.CreateManagerActivateEvent();
        using MainForm form = new();
        form.StartActivationListener(activateEvent);
        Application.Run(form);
        return 0;
    }
}
