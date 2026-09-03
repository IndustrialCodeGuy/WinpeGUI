using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Imaging.Core;

internal readonly record struct DismProcessResult(bool Canceled, int ExitCode, string Output);

internal static class DismProcessRunner
{
    private static readonly Regex PercentRegex = new(
        @"(?<!\d)(?<value>\d{1,3}(?:\.\d+)?)\s*%",
        RegexOptions.Compiled);

    public static async Task<DismProcessResult> RunAsync(
        IEnumerable<string> arguments,
        Action<int?, string>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return new DismProcessResult(true, -1, string.Empty);

        string dismPath = ResolveDismPath();
        ProcessStartInfo startInfo = new()
        {
            FileName = dismPath,
            WorkingDirectory = Path.GetDirectoryName(dismPath) ?? Environment.SystemDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start DISM.exe.");

        StringBuilder output = new();
        object sync = new();
        int? lastPercent = null;
        string lastMessage = string.Empty;

        void consumeLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            string trimmed = line.Trim();
            int? percentage;
            string progressMessage;
            lock (sync)
            {
                output.AppendLine(trimmed);

                int? parsedPercent = TryParsePercent(trimmed);
                string parsedMessage = GetProgressMessage(trimmed, parsedPercent.HasValue);
                if (parsedPercent.HasValue)
                    lastPercent = parsedPercent;
                if (!string.IsNullOrWhiteSpace(parsedMessage))
                    lastMessage = parsedMessage;

                percentage = lastPercent;
                progressMessage = string.IsNullOrWhiteSpace(lastMessage) ? trimmed : lastMessage;
            }

            reportProgress?.Invoke(percentage, progressMessage);
        }

        Task stdoutTask = ReadProgressStreamAsync(process.StandardOutput, consumeLine);
        Task stderrTask = ReadProgressStreamAsync(process.StandardError, consumeLine);

        using CancellationTokenRegistration registration = cancellationToken.Register(() => TryKill(process));
        bool canceled = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            canceled = true;
            TryKill(process);
            try
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        string finalOutput;
        lock (sync)
            finalOutput = output.ToString().Trim();

        return canceled || cancellationToken.IsCancellationRequested
            ? new DismProcessResult(true, -1, finalOutput)
            : new DismProcessResult(false, process.ExitCode, finalOutput);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static async Task ReadProgressStreamAsync(TextReader reader, Action<string> consumeLine)
    {
        char[] buffer = new char[256];
        StringBuilder line = new();

        while (true)
        {
            int read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read <= 0)
                break;

            for (int i = 0; i < read; i++)
            {
                char c = buffer[i];
                if (c is '\r' or '\n')
                {
                    if (line.Length > 0)
                    {
                        consumeLine(line.ToString());
                        line.Clear();
                    }
                    continue;
                }

                line.Append(c);
            }
        }

        if (line.Length > 0)
            consumeLine(line.ToString());
    }

    private static int? TryParsePercent(string line)
    {
        Match match = PercentRegex.Match(line);
        if (!match.Success ||
            !double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return null;
        }

        return Math.Clamp((int)Math.Round(value), 0, 100);
    }

    private static string GetProgressMessage(string line, bool containsPercentage)
    {
        if (containsPercentage)
            return "Processing image...";

        if (line.StartsWith("Deployment Image Servicing", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Image Version:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return line.Length <= 160 ? line : line[..160];
    }

    private static string ResolveDismPath()
    {
        string? systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        List<string> candidates = new();

        if (!string.IsNullOrWhiteSpace(Environment.SystemDirectory))
            candidates.Add(Path.Combine(Environment.SystemDirectory, "dism.exe"));

        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            candidates.Add(Path.Combine(systemRoot, "Sysnative", "dism.exe"));
            candidates.Add(Path.Combine(systemRoot, "System32", "dism.exe"));
        }

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "DISM.exe was not found under the active Windows system directory.",
            "dism.exe");
    }
}
