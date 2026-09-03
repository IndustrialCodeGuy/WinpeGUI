using BitLocker.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal static class Program
{
    private const string InstanceMutexName = @"Local\WinPEGUI.ImagingManager";

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

        using Mutex instanceMutex = new(true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Imaging Manager is already running. Use the existing window to avoid overlapping disk or image operations.",
                "Imaging Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        try
        {
            using MainForm form = new();
            Application.Run(form);
            return 0;
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
    }
}
