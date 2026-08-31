using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Imaging.Core;

public sealed class DismWimBackend
{
    private static readonly Regex PercentRegex = new(@"(?<!\d)(?<value>\d{1,3}(?:\.\d+)?)\s*%", RegexOptions.Compiled);
    private static readonly Regex ImageInfoFieldRegex = new(@"^(?<field>Index|Name|Description)\s*:\s*(?<value>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Task<WimOperationResult> CaptureAsync(
        string sourceRoot,
        string imageFile,
        string imageName,
        string? description,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string captureRoot = ImagingPath.NormalizeDriveRoot(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        if (!Directory.Exists(captureRoot))
            throw new DirectoryNotFoundException($"The capture source is not accessible: {captureRoot}");

        List<string> arguments = new()
        {
            "/Capture-Image",
            $"/ImageFile:{imageFile}",
            $"/CaptureDir:{captureRoot}",
            $"/Name:{imageName}"
        };

        if (!string.IsNullOrWhiteSpace(description))
            arguments.Add($"/Description:{description.Trim()}");

        arguments.Add("/CheckIntegrity");
        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<WimOperationResult> AppendAsync(
        string sourceRoot,
        string imageFile,
        string imageName,
        string? description,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string captureRoot = ImagingPath.NormalizeDriveRoot(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        if (!Directory.Exists(captureRoot))
            throw new DirectoryNotFoundException($"The capture source is not accessible: {captureRoot}");
        if (!File.Exists(imageFile))
            throw new FileNotFoundException("The WIM file to append to was not found.", imageFile);

        List<string> arguments = new()
        {
            "/Append-Image",
            $"/ImageFile:{imageFile}",
            $"/CaptureDir:{captureRoot}",
            $"/Name:{imageName}"
        };

        if (!string.IsNullOrWhiteSpace(description))
            arguments.Add($"/Description:{description.Trim()}");

        arguments.Add("/CheckIntegrity");
        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<WimOperationResult> ApplyAsync(
        string targetRoot,
        string imageFile,
        int imageIndex,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string applyRoot = ImagingPath.NormalizeDriveRoot(targetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(applyRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);

        if (imageIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), "The WIM image index must be greater than zero.");
        if (!Directory.Exists(applyRoot))
            throw new DirectoryNotFoundException($"The apply target is not accessible: {applyRoot}");
        if (!File.Exists(imageFile))
            throw new FileNotFoundException("The WIM file was not found.", imageFile);

        string[] arguments =
        {
            "/Apply-Image",
            $"/ImageFile:{imageFile}",
            $"/Index:{imageIndex}",
            $"/ApplyDir:{applyRoot}",
            "/CheckIntegrity"
        };

        return RunAsync(arguments, progress, cancellationToken);
    }

    public async Task<WimImageInfoResult> GetImagesAsync(string imageFile, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);
        if (!File.Exists(imageFile))
            throw new FileNotFoundException("The WIM file was not found.", imageFile);

        WimOperationResult result = await RunAsync(
            new[]
            {
                "/Get-ImageInfo",
                $"/ImageFile:{imageFile}",
                "/English"
            },
            progress: null,
            cancellationToken).ConfigureAwait(false);

        return new WimImageInfoResult
        {
            Success = result.Success,
            Canceled = result.Canceled,
            ExitCode = result.ExitCode,
            Output = result.Output,
            Images = result.Success ? ParseImageInfo(result.Output) : Array.Empty<WimImageInfo>()
        };
    }

    private async Task<WimOperationResult> RunAsync(
        IEnumerable<string> arguments,
        IProgress<WimOperationProgress>? progress,
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

            progress?.Report(new WimOperationProgress(lastPercent, string.IsNullOrWhiteSpace(lastMessage) ? trimmed : lastMessage));
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
            return WimOperationResult.Cancelled(finalOutput);

        return WimOperationResult.Completed(process.ExitCode, finalOutput);
    }

    private static IReadOnlyList<WimImageInfo> ParseImageInfo(string output)
    {
        List<WimImageInfo> images = new();
        int? index = null;
        string name = string.Empty;
        string description = string.Empty;

        void flush()
        {
            if (!index.HasValue)
                return;

            images.Add(new WimImageInfo
            {
                Index = index.Value,
                Name = name,
                Description = description
            });
            index = null;
            name = string.Empty;
            description = string.Empty;
        }

        using StringReader reader = new(output ?? string.Empty);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            Match match = ImageInfoFieldRegex.Match(line.Trim());
            if (!match.Success)
                continue;

            string field = match.Groups["field"].Value;
            string value = match.Groups["value"].Value.Trim();
            if (field.Equals("Index", StringComparison.OrdinalIgnoreCase))
            {
                flush();
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                    index = parsed;
            }
            else if (field.Equals("Name", StringComparison.OrdinalIgnoreCase) && index.HasValue)
            {
                name = value;
            }
            else if (field.Equals("Description", StringComparison.OrdinalIgnoreCase) && index.HasValue)
            {
                description = value;
            }
        }

        flush();
        return images.OrderBy(static image => image.Index).ToArray();
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
