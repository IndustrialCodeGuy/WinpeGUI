using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Imaging.Core;

public sealed class DismFfuBackend
{
    private static readonly Regex PercentRegex = new(@"(?<!\d)(?<value>\d{1,3}(?:\.\d+)?)\s*%", RegexOptions.Compiled);
    public Task<FfuOperationResult> CaptureAsync(
        ImagingDiskInfo disk,
        string imageFile,
        string imageName,
        string? description,
        IProgress<FfuOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        List<string> arguments = new()
        {
            "/Capture-FFU",
            $"/ImageFile:{imageFile}",
            $"/CaptureDrive:{GetPhysicalDrivePath(disk)}",
            $"/Name:{imageName}"
        };

        if (!string.IsNullOrWhiteSpace(description))
            arguments.Add($"/Description:{description.Trim()}");

        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<FfuOperationResult> ApplyAsync(
        ImagingDiskInfo disk,
        string imageFile,
        IProgress<FfuOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);

        return RunAsync(
            new[]
            {
                "/Apply-FFU",
                $"/ImageFile:{imageFile}",
                $"/ApplyDrive:{GetPhysicalDrivePath(disk)}"
            },
            progress,
            cancellationToken);
    }

    private async Task<FfuOperationResult> RunAsync(
        IEnumerable<string> arguments,
        IProgress<FfuOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
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
            lock (sync)
            {
                output.AppendLine(trimmed);
            }

            int? percent = TryParsePercent(trimmed);
            string message = GetProgressMessage(trimmed);

            if (percent.HasValue)
                lastPercent = percent;
            if (!string.IsNullOrWhiteSpace(message))
                lastMessage = message;

            progress?.Report(new FfuOperationProgress(lastPercent, string.IsNullOrWhiteSpace(lastMessage) ? trimmed : lastMessage));
        }

        Task stdoutTask = ReadProgressStreamAsync(process.StandardOutput, consumeLine);
        Task stderrTask = ReadProgressStreamAsync(process.StandardError, consumeLine);

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        });

        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        string finalOutput;
        lock (sync)
            finalOutput = output.ToString().Trim();

        if (cancellationToken.IsCancellationRequested)
            return FfuOperationResult.Cancelled(finalOutput);

        return FfuOperationResult.Completed(process.ExitCode, finalOutput);
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
                if (c == '\r' || c == '\n')
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

    private static string GetProgressMessage(string line)
    {
        if (TryParsePercent(line).HasValue)
            return "Processing image...";

        if (line.StartsWith("Deployment Image Servicing", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Image Version:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return line.Length <= 160 ? line : line[..160];
    }

    private static string GetPhysicalDrivePath(ImagingDiskInfo disk)
    {
        if (!string.IsNullOrWhiteSpace(disk.DevicePath) &&
            disk.DevicePath.StartsWith(@"\\.\PhysicalDrive", StringComparison.OrdinalIgnoreCase))
        {
            return disk.DevicePath;
        }

        return $@"\\.\PhysicalDrive{disk.DiskNumber}";
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

        throw new FileNotFoundException("DISM.exe was not found under the active Windows system directory.", "dism.exe");
    }
}
