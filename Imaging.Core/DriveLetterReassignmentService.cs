using System.Diagnostics;
using System.Text;

namespace Imaging.Core;

/// <summary>
/// Performs explicit, user-requested C: reassignment for WIM operations.
/// It does not impose any startup drive-letter policy.
/// </summary>
public sealed class DriveLetterReassignmentService
{
    private readonly TemporaryDriveLetterService _temporaryDriveLetters;

    public DriveLetterReassignmentService(TemporaryDriveLetterService temporaryDriveLetters)
    {
        _temporaryDriveLetters = temporaryDriveLetters ?? throw new ArgumentNullException(nameof(temporaryDriveLetters));
    }

    public DriveLetterReassignmentResult ReassignPartitionToC(
        int diskNumber,
        int partitionNumber,
        string currentTargetRoot,
        string applicationBaseDirectory)
    {
        string targetRoot = ImagingPath.NormalizeDriveRoot(currentTargetRoot);
        if (!TryGetDriveLetter(targetRoot, out char targetLetter))
            return DriveLetterReassignmentResult.Failed("The selected target does not currently have a usable drive letter.");

        if (targetLetter == 'C')
            return DriveLetterReassignmentResult.Unchanged(@"C:\");

        string? applicationRoot = ImagingPath.TryGetDriveRootForPath(applicationBaseDirectory);
        if (string.Equals(applicationRoot, @"C:\", StringComparison.OrdinalIgnoreCase) && IsDriveMounted('C'))
        {
            return DriveLetterReassignmentResult.Failed(
                "Imaging Manager is running from C:. The drive letter hosting the running imaging tools cannot be reassigned.");
        }

        bool cWasMounted = IsDriveMounted('C');
        string script;
        if (cWasMounted)
        {
            // The target's current letter becomes the replacement for the old C: volume.
            // Select the target by disk/partition again at the end because removing C:
            // changes the volume namespace but not the physical partition identity.
            script =
                $"select disk {diskNumber}\r\n" +
                $"select partition {partitionNumber}\r\n" +
                $"remove letter={targetLetter}\r\n" +
                "select volume C\r\n" +
                "remove letter=C\r\n" +
                $"assign letter={targetLetter}\r\n" +
                $"select disk {diskNumber}\r\n" +
                $"select partition {partitionNumber}\r\n" +
                "assign letter=C\r\n" +
                "exit\r\n";
        }
        else
        {
            script =
                $"select disk {diskNumber}\r\n" +
                $"select partition {partitionNumber}\r\n" +
                $"remove letter={targetLetter}\r\n" +
                "assign letter=C\r\n" +
                "exit\r\n";
        }

        ProcessResult result;
        try
        {
            result = RunDiskPart(script);
        }
        catch (Exception ex)
        {
            return DriveLetterReassignmentResult.Failed(ex.Message);
        }

        WaitForDriveMounted('C', shouldExist: true);
        if (cWasMounted)
            WaitForDriveMounted(targetLetter, shouldExist: true);

        if (!result.Success || !IsDriveMounted('C'))
        {
            return DriveLetterReassignmentResult.Failed(
                BuildProcessFailure("DiskPart could not reassign the selected partition to C:.", result));
        }

        return new DriveLetterReassignmentResult
        {
            Success = true,
            Changed = true,
            TargetRoot = @"C:\",
            PreviousTargetRoot = targetRoot,
            DisplacedCRoot = cWasMounted ? $"{targetLetter}:\\" : string.Empty
        };
    }

    public DriveLetterReassignmentResult MoveCToLowestAvailable(
        string applicationBaseDirectory,
        params char[] excludedLetters)
    {
        if (!IsDriveMounted('C'))
            return DriveLetterReassignmentResult.Unchanged(@"C:\");

        string? applicationRoot = ImagingPath.TryGetDriveRootForPath(applicationBaseDirectory);
        if (string.Equals(applicationRoot, @"C:\", StringComparison.OrdinalIgnoreCase))
        {
            return DriveLetterReassignmentResult.Failed(
                "Imaging Manager is running from C:. The drive letter hosting the running imaging tools cannot be reassigned.");
        }

        char[] exclusions = excludedLetters
            .Append('C')
            .Append('X')
            .Distinct()
            .ToArray();

        TemporaryDriveLetterReservation reservation;
        try
        {
            reservation = _temporaryDriveLetters.ReserveLowestAvailable(exclusions);
        }
        catch (Exception ex)
        {
            return DriveLetterReassignmentResult.Failed(ex.Message);
        }

        char replacement = reservation.DriveLetter;
        try
        {
            ProcessResult result = RunDiskPart(
                "select volume C\r\n" +
                "remove letter=C\r\n" +
                $"assign letter={replacement}\r\n" +
                "exit\r\n");

            WaitForDriveMounted(replacement, shouldExist: true);
            WaitForDriveMounted('C', shouldExist: false);

            if (!result.Success || !IsDriveMounted(replacement) || IsDriveMounted('C'))
            {
                return DriveLetterReassignmentResult.Failed(
                    BuildProcessFailure($"DiskPart could not move the existing C: volume to {replacement}:.", result));
            }

            return new DriveLetterReassignmentResult
            {
                Success = true,
                Changed = true,
                TargetRoot = @"C:\",
                PreviousTargetRoot = @"C:\",
                DisplacedCRoot = $"{replacement}:\\"
            };
        }
        catch (Exception ex)
        {
            return DriveLetterReassignmentResult.Failed(ex.Message);
        }
        finally
        {
            _temporaryDriveLetters.Release(reservation);
        }
    }

    public static string RebasePathFromDisplacedC(string path, string displacedCRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(displacedCRoot))
            return path;

        string? root = ImagingPath.TryGetDriveRootForPath(path);
        if (!string.Equals(root, @"C:\", StringComparison.OrdinalIgnoreCase))
            return path;

        string replacementRoot = ImagingPath.NormalizeDriveRoot(displacedCRoot);
        return replacementRoot + path[3..];
    }

    private static bool IsDriveMounted(char letter) =>
        Directory.GetLogicalDrives().Any(root =>
            root.Length >= 2 &&
            char.ToUpperInvariant(root[0]) == char.ToUpperInvariant(letter) &&
            root[1] == ':');

    private static bool TryGetDriveLetter(string root, out char letter)
    {
        if (root.Length >= 2 && root[1] == ':' && char.IsLetter(root[0]))
        {
            letter = char.ToUpperInvariant(root[0]);
            return true;
        }

        letter = '\0';
        return false;
    }

    private static ProcessResult RunDiskPart(string script)
    {
        string diskPartPath = Path.Combine(Environment.SystemDirectory, "diskpart.exe");
        if (!File.Exists(diskPartPath))
            throw new FileNotFoundException("DiskPart.exe was not found under the active Windows system directory.", diskPartPath);

        string scriptPath = Path.Combine(Path.GetTempPath(), $"ImagingManager-DriveLetter-{Guid.NewGuid():N}.txt");
        File.WriteAllText(scriptPath, script, Encoding.ASCII);

        try
        {
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
            process.WaitForExit();
            string output = outputTask.GetAwaiter().GetResult().Trim();
            string error = errorTask.GetAwaiter().GetResult().Trim();
            string combined = string.Join(Environment.NewLine, new[] { output, error }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));

            bool success = process.ExitCode == 0 && !ContainsDiskPartFailure(combined);
            return new ProcessResult(success, process.ExitCode, output, error);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static bool ContainsDiskPartFailure(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        string[] markers =
        {
            "DiskPart has encountered an error",
            "Virtual Disk Service error",
            "The arguments specified for this command are not valid",
            "There is no disk selected",
            "There is no partition selected",
            "The selected volume is not valid"
        };

        return markers.Any(marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static void WaitForDriveMounted(char letter, bool shouldExist)
    {
        for (int attempt = 0; attempt < 15 && IsDriveMounted(letter) != shouldExist; attempt++)
            Thread.Sleep(100);
    }

    private static string BuildProcessFailure(string message, ProcessResult result)
    {
        string detail = string.Join(Environment.NewLine, new[] { result.StandardOutput, result.StandardError }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(detail) ? message : message + "\n\n" + detail;
    }

    private readonly record struct ProcessResult(
        bool Success,
        int ExitCode,
        string StandardOutput,
        string StandardError);
}

public sealed class DriveLetterReassignmentResult
{
    public bool Success { get; init; }
    public bool Changed { get; init; }
    public string TargetRoot { get; init; } = string.Empty;
    public string PreviousTargetRoot { get; init; } = string.Empty;
    public string DisplacedCRoot { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;

    public static DriveLetterReassignmentResult Unchanged(string targetRoot) => new()
    {
        Success = true,
        Changed = false,
        TargetRoot = ImagingPath.NormalizeDriveRoot(targetRoot),
        PreviousTargetRoot = ImagingPath.NormalizeDriveRoot(targetRoot)
    };

    public static DriveLetterReassignmentResult Failed(string error) => new()
    {
        Success = false,
        Error = error
    };
}
