using System.Diagnostics;
using System.Text;

namespace Imaging.Core;

public sealed class PartitionFormatService
{
    private static readonly HashSet<string> SupportedFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "NTFS",
        "FAT",
        "FAT32",
        "exFAT",
        "ReFS"
    };

    public PartitionFileSystemResult GetCurrentFileSystem(string targetRoot)
    {
        string root = ImagingPath.NormalizeDriveRoot(targetRoot);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return PartitionFileSystemResult.Failed("The selected partition is not currently accessible.");

        try
        {
            DriveInfo drive = new(root);
            string fileSystem = drive.DriveFormat?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(fileSystem))
                return PartitionFileSystemResult.Failed("Windows did not report a filesystem for the selected partition.");

            if (!SupportedFileSystems.Contains(fileSystem))
            {
                return PartitionFileSystemResult.Failed(
                    $"The selected partition uses the unsupported filesystem '{fileSystem}'. Imaging Manager will not guess a replacement filesystem before applying the WIM.");
            }

            return new PartitionFileSystemResult
            {
                Success = true,
                FileSystem = fileSystem
            };
        }
        catch (Exception ex)
        {
            return PartitionFileSystemResult.Failed(
                "Imaging Manager could not determine the current filesystem of the selected partition. " +
                "Unlock the volume first if it is BitLocker-protected.\n\n" + ex.Message);
        }
    }

    public async Task<PartitionFormatResult> FormatQuickAsync(
        string targetRoot,
        string fileSystem,
        CancellationToken cancellationToken)
    {
        string root = ImagingPath.NormalizeDriveRoot(targetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileSystem);

        if (!SupportedFileSystems.Contains(fileSystem))
            return PartitionFormatResult.Failed($"The filesystem '{fileSystem}' is not supported for automatic formatting.");

        string diskPartPath = Path.Combine(Environment.SystemDirectory, "diskpart.exe");
        if (!File.Exists(diskPartPath))
            return PartitionFormatResult.Failed("DiskPart.exe was not found under the active Windows system directory.");

        if (root.Length < 3 || root[1] != ':' || !char.IsLetter(root[0]))
        {
            return PartitionFormatResult.Failed(
                $"The selected partition does not have a usable drive-letter root for formatting: {root}");
        }

        // Format the exact volume that DISM will use rather than translating the
        // WMI partition index into a DiskPart partition number. This avoids any
        // numbering mismatch and also works for temporarily assigned drive letters.
        char driveLetter = char.ToUpperInvariant(root[0]);
        string markerPath = Path.Combine(root, $".ImagingManager-FormatVerify-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                markerPath,
                "Imaging Manager format verification marker.",
                Encoding.ASCII,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return PartitionFormatResult.Failed(
                $"Imaging Manager could not create a format-verification marker on {root}. " +
                "The target may be read-only or otherwise unavailable.\n\n" + ex.Message);
        }

        string scriptPath = Path.Combine(Path.GetTempPath(), $"ImagingManager-Format-{Guid.NewGuid():N}.txt");
        string script =
            $"select volume {driveLetter}\r\n" +
            $"format quick fs={fileSystem} override\r\n" +
            "exit\r\n";

        try
        {
            await File.WriteAllTextAsync(scriptPath, script, Encoding.ASCII, cancellationToken).ConfigureAwait(false);

            ProcessStartInfo startInfo = new()
            {
                FileName = diskPartPath,
                WorkingDirectory = Path.GetDirectoryName(diskPartPath) ?? Environment.SystemDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add(scriptPath);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start DiskPart.exe.");

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }

                try { await process.WaitForExitAsync().ConfigureAwait(false); } catch { }
                try { await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false); } catch { }
                throw;
            }

            string output = (await outputTask.ConfigureAwait(false)).Trim();
            string error = (await errorTask.ConfigureAwait(false)).Trim();
            if (process.ExitCode != 0)
            {
                return PartitionFormatResult.Failed(
                    BuildFailure("DiskPart could not format the selected partition.", output, error),
                    process.ExitCode,
                    output);
            }

            // DiskPart script execution does not give us a sufficiently strong
            // success signal by process exit code alone. If the marker still
            // exists, the target volume was not actually reformatted and DISM
            // must not be allowed to overlay the existing filesystem.
            if (File.Exists(markerPath))
            {
                return PartitionFormatResult.Failed(
                    BuildFailure(
                        $"DiskPart returned without reformatting the selected target volume {driveLetter}:.",
                        output,
                        error),
                    process.ExitCode,
                    output);
            }

            // Give WinPE a moment to refresh the newly formatted filesystem before DISM starts.
            for (int attempt = 0; attempt < 10 && !Directory.Exists(root); attempt++)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            if (!Directory.Exists(root))
            {
                return PartitionFormatResult.Failed(
                    "DiskPart completed, but the formatted target partition is no longer accessible at " + root,
                    process.ExitCode,
                    output);
            }

            try
            {
                string actualFileSystem = new DriveInfo(root).DriveFormat?.Trim() ?? string.Empty;
                if (!actualFileSystem.Equals(fileSystem, StringComparison.OrdinalIgnoreCase))
                {
                    return PartitionFormatResult.Failed(
                        $"The target partition reported filesystem '{actualFileSystem}' after formatting; '{fileSystem}' was expected.",
                        process.ExitCode,
                        output);
                }
            }
            catch (Exception ex)
            {
                return PartitionFormatResult.Failed(
                    "The target partition was formatted, but Imaging Manager could not verify the resulting filesystem.\n\n" + ex.Message,
                    process.ExitCode,
                    output);
            }

            return new PartitionFormatResult
            {
                Success = true,
                ExitCode = process.ExitCode,
                Output = output,
                FileSystem = fileSystem
            };
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
            try { File.Delete(markerPath); } catch { }
        }
    }

    private static string BuildFailure(string message, string output, string error)
    {
        string detail = string.Join(Environment.NewLine, new[] { output, error }
            .Where(static s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(detail) ? message : message + "\n\n" + detail;
    }
}

public sealed class PartitionFileSystemResult
{
    public bool Success { get; init; }
    public string FileSystem { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;

    public static PartitionFileSystemResult Failed(string error) => new()
    {
        Success = false,
        Error = error
    };
}

public sealed class PartitionFormatResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
    public string FileSystem { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;

    public static PartitionFormatResult Failed(string error, int exitCode = -1, string output = "") => new()
    {
        Success = false,
        ExitCode = exitCode,
        Output = output,
        Error = error
    };
}
