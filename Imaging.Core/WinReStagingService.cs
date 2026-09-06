using System.Text.RegularExpressions;

namespace Imaging.Core;

public sealed class WinReStagingService
{
    private static readonly Regex RecoveryLocationRegex = new(
        @"(?<location>\\\\\?\\GLOBALROOT\\device\\harddisk(?<disk>\d+)\\partition(?<partition>\d+)(?<relative>\\[^\r\n]*))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly TemporaryDriveLetterService _temporaryDriveLetters;

    public WinReStagingService(TemporaryDriveLetterService temporaryDriveLetters)
    {
        _temporaryDriveLetters = temporaryDriveLetters ?? throw new ArgumentNullException(nameof(temporaryDriveLetters));
    }

    public string GetWindowsDirectory(string sourceRoot)
    {
        string root = ImagingPath.NormalizeDriveRoot(sourceRoot);
        return Path.Combine(root, "Windows");
    }

    public string GetWinRePath(string sourceRoot) =>
        Path.Combine(GetWindowsDirectory(sourceRoot), "System32", "Recovery", "winre.wim");

    public bool IsWindowsInstallation(string sourceRoot) =>
        Directory.Exists(Path.Combine(GetWindowsDirectory(sourceRoot), "System32"));

    public async Task<WinReStageResult> StageFromConfiguredRecoveryPartitionAsync(
        string sourceRoot,
        CancellationToken cancellationToken)
    {
        string root = ImagingPath.NormalizeDriveRoot(sourceRoot);
        if (root.Length == 0)
            return WinReStageResult.Failed("The selected partition does not have a usable drive letter.");

        string destination = GetWinRePath(root);
        if (File.Exists(destination))
            return WinReStageResult.AlreadyPresent(destination);

        RecoveryLocationResult location = await FindConfiguredRecoveryLocationAsync(root, cancellationToken).ConfigureAwait(false);
        if (!location.Success)
            return WinReStageResult.Failed(location.Error);

        TemporaryDriveLetterResult? assignment = null;
        bool copied = false;
        string stagingError = string.Empty;
        string? removalError = null;

        try
        {
            assignment = await _temporaryDriveLetters.AssignAsync(
                location.DiskNumber,
                location.PartitionNumber,
                cancellationToken).ConfigureAwait(false);
            if (!assignment.Success)
            {
                stagingError =
                    "Imaging Manager could not temporarily assign an accessible drive letter to the configured Windows RE partition.\n\n" +
                    assignment.Error;
            }
            else
            {
                string relativeDirectory = NormalizeRecoveryRelativePath(location.RelativePath);
                string source = Path.Combine(assignment.Root, relativeDirectory.TrimStart('\\'), "winre.wim");
                if (!File.Exists(source))
                {
                    stagingError =
                        $"The configured Windows RE partition was mounted as {assignment.DriveLetter}:, but winre.wim was not found at:\n\n{source}";
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await CopyFileAsync(source, destination, cancellationToken).ConfigureAwait(false);
                    copied = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stagingError = ex.Message;
        }
        finally
        {
            if (assignment?.Success == true)
            {
                removalError = await _temporaryDriveLetters.RemoveAsync(
                    assignment,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        if (!copied)
        {
            if (string.IsNullOrWhiteSpace(stagingError))
                stagingError = "Windows RE staging did not complete.";

            if (!string.IsNullOrWhiteSpace(removalError))
            {
                stagingError +=
                    "\n\nIMPORTANT: Cleanup also failed after the staging attempt. The Recovery partition may still have a temporary drive letter assigned.\n\n" +
                    removalError;
            }

            return WinReStageResult.Failed(stagingError);
        }

        return new WinReStageResult
        {
            Success = true,
            StagedByImagingManager = true,
            WinRePath = destination,
            Warning = removalError ?? string.Empty
        };
    }

    public Task RemoveStagedWinReAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        string path = GetWinRePath(sourceRoot);
        if (!File.Exists(path))
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(path);
        }, cancellationToken);
    }

    private async Task<RecoveryLocationResult> FindConfiguredRecoveryLocationAsync(
        string sourceRoot,
        CancellationToken cancellationToken)
    {
        string windowsDirectory = GetWindowsDirectory(sourceRoot);
        string systemReagentc = Path.Combine(Environment.SystemDirectory, "reagentc.exe");
        string targetReagentc = Path.Combine(windowsDirectory, "System32", "reagentc.exe");
        string reagentc = File.Exists(systemReagentc)
            ? systemReagentc
            : targetReagentc;

        if (!File.Exists(reagentc))
        {
            return RecoveryLocationResult.Failed(
                "REAgentC.exe was not found, so the configured Windows RE partition could not be determined.");
        }

        ProcessExecutionResult result = await ProcessExecutionRunner.RunAsync(
            reagentc,
            new[] { "/info", "/target", windowsDirectory },
            cancellationToken).ConfigureAwait(false);
        string combined = result.CombinedOutput;

        Match match = RecoveryLocationRegex.Match(combined);
        if (!match.Success ||
            !int.TryParse(match.Groups["disk"].Value, out int diskNumber) ||
            !int.TryParse(match.Groups["partition"].Value, out int partitionNumber))
        {
            string detail = string.IsNullOrWhiteSpace(combined)
                ? "REAgentC did not report a configured Windows RE location."
                : "REAgentC did not report a usable Windows RE partition location.\n\n" + combined.Trim();
            return RecoveryLocationResult.Failed(detail);
        }

        return new RecoveryLocationResult
        {
            Success = true,
            DiskNumber = diskNumber,
            PartitionNumber = partitionNumber,
            RelativePath = match.Groups["relative"].Value
        };
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        const int BufferSize = 1024 * 1024;
        await using FileStream sourceStream = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream destinationStream = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            await sourceStream.CopyToAsync(destinationStream, BufferSize, cancellationToken).ConfigureAwait(false);
            await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { destinationStream.Close(); } catch { }
            try { File.Delete(destination); } catch { }
            throw;
        }
    }

    private static string NormalizeRecoveryRelativePath(string relativePath)
    {
        string path = (relativePath ?? string.Empty).Trim();
        if (path.Length == 0 || path == "\\")
            return @"\Recovery\WindowsRE";

        return path.TrimEnd('\\');
    }

    private sealed class RecoveryLocationResult
    {
        public bool Success { get; init; }
        public int DiskNumber { get; init; }
        public int PartitionNumber { get; init; }
        public string RelativePath { get; init; } = string.Empty;
        public string Error { get; init; } = string.Empty;

        public static RecoveryLocationResult Failed(string error) => new() { Success = false, Error = error };
    }
}

public sealed class WinReStageResult
{
    public bool Success { get; init; }
    public bool StagedByImagingManager { get; init; }
    public string WinRePath { get; init; } = string.Empty;
    public string Warning { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;

    public static WinReStageResult AlreadyPresent(string path) => new()
    {
        Success = true,
        StagedByImagingManager = false,
        WinRePath = path
    };

    public static WinReStageResult Failed(string error) => new()
    {
        Success = false,
        Error = error
    };
}
