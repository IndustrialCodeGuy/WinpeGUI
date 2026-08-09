using Shared.Shell.Theming;

namespace Shell.Taskbar.Host;

internal static class Program
{
    private const string SessionOwnerPidArg = "--session-owner-pid";

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        ShellTheme.ConfigureFromArgs(args);

        Application.Run(new TaskbarApplicationContext(ParseSessionOwnerProcessId(args)));
    }

    private static int ParseSessionOwnerProcessId(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(SessionOwnerPidArg, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 < args.Length && int.TryParse(args[i + 1], out int processId))
                return processId;

            return 0;
        }

        return 0;
    }
}
