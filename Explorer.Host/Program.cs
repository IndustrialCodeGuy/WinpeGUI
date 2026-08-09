using Explorer.Host.Startup;
using Shell.Core.Host;
using Shell.Core.Models;
using Shell.Infrastructure.FileAssociations;
using Shared.Shell.Theming;

namespace Explorer.Host;

internal static class Program
{
    private const string SessionOwnerPidArg = "--session-owner-pid";

    [STAThread]
    static void Main(string[] args)
    {
        ExplorerLaunchRequest launchRequest = BuildLaunchRequest(args);
        int sessionOwnerProcessId = ParseSessionOwnerProcessId(args);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        ShellTheme.ConfigureFromArgs(args);

        using SingleInstanceGate gate = new(@"Local\ExplorerHost.SingleInstance");

        if (!gate.IsPrimaryInstance)
        {
            if (launchRequest.HostOnly)
                return;

            if (!ExplorerHostClient.TrySignalOpenWindow(launchRequest))
            {
                MessageBox.Show(
                    "Explorer is already running, but this instance could not contact the running shell to open the requested window.\r\n\r\nThe existing shell was left running.",
                    "Explorer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return;
        }

        PeFileAssociationRegistryWriter.ApplyIfWinPE();

        Application.Run(new ExplorerApplicationContext(launchRequest, sessionOwnerProcessId));
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

    private static ExplorerLaunchRequest BuildLaunchRequest(string[] args)
    {
        string? initialPath = null;
        string? title = null;
        ExplorerWindowMode mode = ExplorerWindowMode.Browse;
        bool hostOnly = false;
        List<string> allowedExtensions = [];

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            string lower = arg.ToLowerInvariant();

            switch (lower)
            {
                case "-host":
                case "--host":
                    hostOnly = true;
                    continue;

                case "--openfile":
                    mode = ExplorerWindowMode.OpenFile;
                    continue;

                case "--selectfolder":
                    mode = ExplorerWindowMode.SelectFolder;
                    continue;

                case "--savefile":
                    mode = ExplorerWindowMode.SaveFile;
                    continue;

                case "--initial" when i + 1 < args.Length:
                case "--path" when i + 1 < args.Length:
                    initialPath = args[++i];
                    continue;

                case "--title" when i + 1 < args.Length:
                    title = args[++i];
                    continue;

                case "--filter" when i + 1 < args.Length:
                case "--filters" when i + 1 < args.Length:
                case "--extension" when i + 1 < args.Length:
                case "--extensions" when i + 1 < args.Length:
                    AddExtensions(allowedExtensions, args[++i]);
                    continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    i++;

                continue;
            }

            initialPath ??= arg;
        }

        return new ExplorerLaunchRequest
        {
            InitialPath = initialPath,
            Mode = mode,
            HostOnly = hostOnly,
            Title = title,
            AllowedExtensions = allowedExtensions.ToArray()
        };
    }

    private static void AddExtensions(List<string> target, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return;

        foreach (string part in rawValue.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string extension = part.Trim();
            if (string.IsNullOrWhiteSpace(extension))
                continue;

            if (!extension.StartsWith('.'))
                extension = "." + extension;

            if (!target.Contains(extension, StringComparer.OrdinalIgnoreCase))
                target.Add(extension);
        }
    }
}
