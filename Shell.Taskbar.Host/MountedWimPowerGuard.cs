using System.Diagnostics;

namespace Shell.Taskbar.Host;

internal sealed class MountedWimPowerImage
{
    public string MountDirectory { get; init; } = string.Empty;
    public string ImageFile { get; init; } = string.Empty;
    public int ImageIndex { get; init; }
    public bool ReadWrite { get; init; }
    public string Status { get; init; } = string.Empty;

    public string DisplayText
    {
        get
        {
            string fileName = string.IsNullOrWhiteSpace(ImageFile) ? "Mounted WIM" : Path.GetFileName(ImageFile);
            string index = ImageIndex > 0 ? $" [{ImageIndex}]" : string.Empty;
            string status = string.IsNullOrWhiteSpace(Status) ? "Status unknown" : Status;
            return $"{fileName}{index}  —  {MountDirectory}  —  {status}";
        }
    }
}

internal sealed class MountedWimPowerProbeResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
    public IReadOnlyList<MountedWimPowerImage> Images { get; init; } = Array.Empty<MountedWimPowerImage>();
}

internal static class MountedWimPowerGuard
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    public static async Task<MountedWimPowerProbeResult> ProbeAsync()
    {
        string dismPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dism.exe");
        if (!File.Exists(dismPath))
        {
            return new MountedWimPowerProbeResult
            {
                Success = false,
                Error = $"DISM.exe was not found at {dismPath}."
            };
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = dismPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/Get-MountedImageInfo");
        startInfo.ArgumentList.Add("/English");

        try
        {
            using Process process = new() { StartInfo = startInfo };
            if (!process.Start())
            {
                return new MountedWimPowerProbeResult
                {
                    Success = false,
                    Error = "DISM could not be started."
                };
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            using CancellationTokenSource timeout = new(ProbeTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return new MountedWimPowerProbeResult
                {
                    Success = false,
                    Error = "The mounted-WIM check timed out."
                };
            }

            string output = await outputTask.ConfigureAwait(true);
            string error = await errorTask.ConfigureAwait(true);
            if (process.ExitCode != 0)
            {
                string details = string.IsNullOrWhiteSpace(error) ? output : error;
                return new MountedWimPowerProbeResult
                {
                    Success = false,
                    Error = string.IsNullOrWhiteSpace(details)
                        ? $"DISM exited with code {process.ExitCode}."
                        : details.Trim()
                };
            }

            return new MountedWimPowerProbeResult
            {
                Success = true,
                Images = ParseMountedWims(output)
            };
        }
        catch (Exception ex)
        {
            return new MountedWimPowerProbeResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static IReadOnlyList<MountedWimPowerImage> ParseMountedWims(string output)
    {
        List<MountedWimPowerImage> images = new();
        string mountDirectory = string.Empty;
        string imageFile = string.Empty;
        int imageIndex = 0;
        bool readWrite = false;
        string status = string.Empty;

        void flush()
        {
            if (!string.IsNullOrWhiteSpace(mountDirectory) &&
                imageFile.EndsWith(".wim", StringComparison.OrdinalIgnoreCase))
            {
                images.Add(new MountedWimPowerImage
                {
                    MountDirectory = mountDirectory,
                    ImageFile = imageFile,
                    ImageIndex = imageIndex,
                    ReadWrite = readWrite,
                    Status = status
                });
            }

            mountDirectory = string.Empty;
            imageFile = string.Empty;
            imageIndex = 0;
            readWrite = false;
            status = string.Empty;
        }

        using StringReader reader = new(output ?? string.Empty);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            int separator = trimmed.IndexOf(':');
            if (separator <= 0)
                continue;

            string field = trimmed[..separator].Trim();
            string value = trimmed[(separator + 1)..].Trim();
            if (field.Equals("Mount Dir", StringComparison.OrdinalIgnoreCase))
            {
                flush();
                mountDirectory = value;
            }
            else if (field.Equals("Image File", StringComparison.OrdinalIgnoreCase))
            {
                imageFile = value;
            }
            else if (field.Equals("Image Index", StringComparison.OrdinalIgnoreCase))
            {
                _ = int.TryParse(value, out imageIndex);
            }
            else if (field.Equals("Mounted Read/Write", StringComparison.OrdinalIgnoreCase))
            {
                readWrite = value.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                            value.Equals("True", StringComparison.OrdinalIgnoreCase);
            }
            else if (field.Equals("Status", StringComparison.OrdinalIgnoreCase))
            {
                status = value;
            }
        }

        flush();
        return images;
    }
}
