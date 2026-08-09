using Shell.Core.Models;
using Shell.Core.Pickers;
using System.Globalization;

namespace ExplorerPicker;

internal static class Program
{
    private const int ExitAccepted = 0;
    private const int ExitCanceled = 1;
    private const int ExitError = 2;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(IsHelpOption))
        {
            WriteUsage(Console.Out);
            return ExitAccepted;
        }

        if (!TryParseArgs(args, out PickerCommand command, out string? errorMessage))
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
                Console.Error.WriteLine(errorMessage);

            WriteUsage(Console.Error);
            return ExitError;
        }

        ExplorerPickerResult result = ExplorerPickerClient.Pick(new ExplorerPickerRequest
        {
            Mode = command.Mode!.Value,
            InitialPath = command.InitialPath,
            Title = command.Title,
            OwnerWindowHandle = command.OwnerWindowHandle,
            AllowedExtensions = command.AllowedExtensions.ToArray()
        }, command.ConnectTimeout);

        if (!result.Accepted)
        {
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                Console.Error.WriteLine(result.ErrorMessage);
                return ExitError;
            }

            return ExitCanceled;
        }

        if (string.IsNullOrWhiteSpace(result.SelectedPath))
        {
            Console.Error.WriteLine("The Explorer picker accepted without returning a path.");
            return ExitError;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(command.ResultFile))
            {
                string? directory = Path.GetDirectoryName(command.ResultFile);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(command.ResultFile, result.SelectedPath + Environment.NewLine);
            }

            Console.Out.WriteLine(result.SelectedPath);
            return ExitAccepted;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unable to write the Explorer picker result.\n\n{ex.Message}");
            return ExitError;
        }
    }

    private static bool IsHelpOption(string arg)
    {
        string value = arg?.Trim() ?? string.Empty;
        return value.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("/?", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseArgs(string[] args, out PickerCommand command, out string? errorMessage)
    {
        command = new PickerCommand();
        errorMessage = null;

        if (args.Length == 0)
        {
            errorMessage = "No picker mode was specified.";
            return false;
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            string lower = arg.ToLowerInvariant();
            switch (lower)
            {
                case "--openfile":
                    if (!TrySetMode(command, ExplorerWindowMode.OpenFile, out errorMessage))
                        return false;
                    continue;

                case "--savefile":
                    if (!TrySetMode(command, ExplorerWindowMode.SaveFile, out errorMessage))
                        return false;
                    continue;

                case "--selectfolder":
                    if (!TrySetMode(command, ExplorerWindowMode.SelectFolder, out errorMessage))
                        return false;
                    continue;

                case "--initial" when i + 1 < args.Length:
                case "--path" when i + 1 < args.Length:
                    command.InitialPath = args[++i];
                    continue;

                case "--title" when i + 1 < args.Length:
                    command.Title = args[++i];
                    continue;

                case "--filter" when i + 1 < args.Length:
                case "--filters" when i + 1 < args.Length:
                case "--extension" when i + 1 < args.Length:
                case "--extensions" when i + 1 < args.Length:
                    AddExtensions(command.AllowedExtensions, args[++i]);
                    continue;

                case "--result-file" when i + 1 < args.Length:
                case "--resultfile" when i + 1 < args.Length:
                    command.ResultFile = args[++i];
                    continue;

                case "--owner-hwnd" when i + 1 < args.Length:
                case "--owner" when i + 1 < args.Length:
                    if (!TryParseWindowHandle(args[++i], out long ownerWindowHandle))
                    {
                        errorMessage = "The owner window handle was not valid.";
                        return false;
                    }

                    command.OwnerWindowHandle = ownerWindowHandle;
                    continue;

                case "--timeout-ms" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeoutMs) || timeoutMs <= 0)
                    {
                        errorMessage = "The timeout must be a positive number of milliseconds.";
                        return false;
                    }

                    command.ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs);
                    continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                errorMessage = $"Unknown Explorer picker option: {arg}";
                return false;
            }

            command.InitialPath ??= arg;
        }

        if (command.Mode is null)
        {
            errorMessage = "No picker mode was specified.";
            return false;
        }

        return true;
    }

    private static bool TrySetMode(PickerCommand command, ExplorerWindowMode mode, out string? errorMessage)
    {
        if (command.Mode is not null && command.Mode != mode)
        {
            errorMessage = "Only one picker mode can be specified.";
            return false;
        }

        command.Mode = mode;
        errorMessage = null;
        return true;
    }

    private static bool TryParseWindowHandle(string value, out long handle)
    {
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(
                value[2..],
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out handle);
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out handle);
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

            if (extension.StartsWith("*", StringComparison.Ordinal))
                extension = extension[1..].TrimStart();

            if (string.IsNullOrWhiteSpace(extension))
                continue;

            if (!extension.StartsWith(".", StringComparison.Ordinal))
                extension = "." + extension;

            if (!target.Contains(extension, StringComparer.OrdinalIgnoreCase))
                target.Add(extension);
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  ExplorerPicker.exe --openfile [--initial <path>] [--title <title>] [--filter <exts>] [--result-file <path>]");
        writer.WriteLine("  ExplorerPicker.exe --savefile [--initial <path>] [--title <title>] [--filter <exts>] [--result-file <path>]");
        writer.WriteLine("  ExplorerPicker.exe --selectfolder [--initial <path>] [--title <title>] [--result-file <path>]");
        writer.WriteLine();
        writer.WriteLine("Exit codes:");
        writer.WriteLine("  0  accepted; selected path written to stdout and optionally --result-file");
        writer.WriteLine("  1  canceled");
        writer.WriteLine("  2  error");
    }

    private sealed class PickerCommand
    {
        public ExplorerWindowMode? Mode { get; set; }
        public string? InitialPath { get; set; }
        public string? Title { get; set; }
        public List<string> AllowedExtensions { get; } = [];
        public string? ResultFile { get; set; }
        public long OwnerWindowHandle { get; set; }
        public TimeSpan? ConnectTimeout { get; set; }
    }
}
