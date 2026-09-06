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

    public async Task<DriveLetterReassignmentResult> ReassignPartitionToCAsync(
        int diskNumber,
        int partitionNumber,
        string currentTargetRoot,
        string applicationBaseDirectory,
        CancellationToken cancellationToken)
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

        // Move an existing C: to a separate free letter first instead of trying to
        // swap two live letters in one DiskPart script. That gives us a stable
        // rollback point if assigning C: to the target fails partway through.
        DriveLetterReassignmentResult? displacedC = null;
        if (IsDriveMounted('C'))
        {
            displacedC = await MoveCToLowestAvailableAsync(
                applicationBaseDirectory,
                cancellationToken,
                targetLetter).ConfigureAwait(false);
            if (!displacedC.Success)
                return displacedC;
        }

        try
        {
            ProcessExecutionResult result = await DiskPartRunner.RunAsync(
                $"select disk {diskNumber}\r\n" +
                $"select partition {partitionNumber}\r\n" +
                $"remove letter={targetLetter}\r\n" +
                "assign letter=C\r\n" +
                "exit\r\n",
                cancellationToken).ConfigureAwait(false);

            await WaitForDriveMountedAsync('C', shouldExist: true, cancellationToken).ConfigureAwait(false);
            if (!result.Success || !IsDriveMounted('C'))
            {
                string error = BuildProcessFailure(
                    "DiskPart could not reassign the selected partition to C:.",
                    result);
                error += await RollbackFailedTargetToCAsync(
                    diskNumber,
                    partitionNumber,
                    targetLetter,
                    displacedC).ConfigureAwait(false);
                return DriveLetterReassignmentResult.Failed(error);
            }
        }
        catch (OperationCanceledException)
        {
            _ = await RollbackFailedTargetToCAsync(
                diskNumber,
                partitionNumber,
                targetLetter,
                displacedC).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            string rollbackDetail = await RollbackFailedTargetToCAsync(
                diskNumber,
                partitionNumber,
                targetLetter,
                displacedC).ConfigureAwait(false);
            return DriveLetterReassignmentResult.Failed(ex.Message + rollbackDetail);
        }

        return new DriveLetterReassignmentResult
        {
            Success = true,
            Changed = true,
            TargetRoot = @"C:\",
            PreviousTargetRoot = targetRoot,
            DisplacedCRoot = displacedC?.DisplacedCRoot ?? string.Empty
        };
    }

    public async Task<DriveLetterReassignmentResult> MoveCToLowestAvailableAsync(
        string applicationBaseDirectory,
        CancellationToken cancellationToken,
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
            ProcessExecutionResult result = await DiskPartRunner.RunAsync(
                "select volume C\r\n" +
                "remove letter=C\r\n" +
                $"assign letter={replacement}\r\n" +
                "exit\r\n",
                cancellationToken).ConfigureAwait(false);

            await WaitForDriveMountedAsync(replacement, shouldExist: true, cancellationToken).ConfigureAwait(false);
            await WaitForDriveMountedAsync('C', shouldExist: false, cancellationToken).ConfigureAwait(false);

            if (!result.Success || !IsDriveMounted(replacement) || IsDriveMounted('C'))
            {
                string error = BuildProcessFailure(
                    $"DiskPart could not move the existing C: volume to {replacement}:.",
                    result);
                string? rollbackError = await TryRollbackFailedCMoveAsync(replacement).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(rollbackError))
                    error += "\n\nRollback warning:\n" + rollbackError;
                return DriveLetterReassignmentResult.Failed(error);
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
        catch (OperationCanceledException)
        {
            _ = await TryRollbackFailedCMoveAsync(replacement).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            string error = ex.Message;
            string? rollbackError = await TryRollbackFailedCMoveAsync(replacement).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(rollbackError))
                error += "\n\nRollback warning:\n" + rollbackError;
            return DriveLetterReassignmentResult.Failed(error);
        }
        finally
        {
            _temporaryDriveLetters.Release(reservation);
        }
    }

    private async Task<string> RollbackFailedTargetToCAsync(
        int diskNumber,
        int partitionNumber,
        char previousTargetLetter,
        DriveLetterReassignmentResult? displacedC)
    {
        List<string> rollbackErrors = new();

        try
        {
            ProcessExecutionResult targetRestore = await DiskPartRunner.RunAsync(
                $"select disk {diskNumber}\r\n" +
                $"select partition {partitionNumber}\r\n" +
                "remove letter=C noerr\r\n" +
                $"assign letter={previousTargetLetter} noerr\r\n" +
                "exit\r\n",
                CancellationToken.None).ConfigureAwait(false);
            await WaitForDriveMountedAsync(previousTargetLetter, shouldExist: true, CancellationToken.None).ConfigureAwait(false);
            if (!targetRestore.Success || !IsDriveMounted(previousTargetLetter))
            {
                rollbackErrors.Add(BuildProcessFailure(
                    $"The target partition could not be restored to {previousTargetLetter}:.",
                    targetRestore));
            }
        }
        catch (Exception ex)
        {
            rollbackErrors.Add($"The target partition could not be restored to {previousTargetLetter}:.\n\n{ex.Message}");
        }

        if (displacedC is { Changed: true })
        {
            string? cRollback = await RollbackMoveCToLowestAvailableAsync(displacedC).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(cRollback))
                rollbackErrors.Add(cRollback);
        }

        return rollbackErrors.Count == 0
            ? string.Empty
            : "\n\nImaging Manager also encountered a problem restoring the original drive-letter assignments:\n" +
              string.Join("\n\n", rollbackErrors);
    }

    private static async Task<string?> TryRollbackFailedCMoveAsync(char replacement)
    {
        bool cMounted = IsDriveMounted('C');
        bool replacementMounted = IsDriveMounted(replacement);
        if (!replacementMounted)
        {
            return cMounted
                ? null
                : $"Neither C: nor {replacement}: is mounted, so the original C: volume could not be identified automatically.";
        }

        try
        {
            string script = cMounted
                ? $"select volume {replacement}\r\nremove letter={replacement} noerr\r\nexit\r\n"
                : $"select volume {replacement}\r\nremove letter={replacement} noerr\r\nassign letter=C\r\nexit\r\n";
            ProcessExecutionResult rollback = await DiskPartRunner.RunAsync(script, CancellationToken.None).ConfigureAwait(false);
            await WaitForDriveMountedAsync(replacement, shouldExist: false, CancellationToken.None).ConfigureAwait(false);
            if (!cMounted)
                await WaitForDriveMountedAsync('C', shouldExist: true, CancellationToken.None).ConfigureAwait(false);

            if (!rollback.Success || IsDriveMounted(replacement) || !IsDriveMounted('C'))
            {
                return BuildProcessFailure(
                    $"DiskPart could not fully restore the original C: assignment after the failed move to {replacement}:.",
                    rollback);
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"DiskPart could not restore the original C: assignment after the failed move to {replacement}:.\n\n{ex.Message}";
        }
    }

    public async Task<string?> RollbackMoveCToLowestAvailableAsync(DriveLetterReassignmentResult reassignment)
    {
        ArgumentNullException.ThrowIfNull(reassignment);
        if (!reassignment.Changed || string.IsNullOrWhiteSpace(reassignment.DisplacedCRoot))
            return null;

        string displacedRoot = ImagingPath.NormalizeDriveRoot(reassignment.DisplacedCRoot);
        if (!TryGetDriveLetter(displacedRoot, out char displacedLetter))
            return "The displaced C: drive letter could not be identified for rollback.";

        try
        {
            ProcessExecutionResult result = await DiskPartRunner.RunAsync(
                $"select volume {displacedLetter}\r\n" +
                $"remove letter={displacedLetter}\r\n" +
                "assign letter=C\r\n" +
                "exit\r\n",
                CancellationToken.None).ConfigureAwait(false);

            await WaitForDriveMountedAsync('C', shouldExist: true, CancellationToken.None).ConfigureAwait(false);
            if (!result.Success || !IsDriveMounted('C'))
            {
                return BuildProcessFailure(
                    $"DiskPart could not restore the displaced {displacedLetter}: volume to C:.",
                    result);
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"DiskPart could not restore the displaced {displacedLetter}: volume to C:.\n\n{ex.Message}";
        }
    }

    public async Task<string?> RollbackPartitionReassignmentToCAsync(
        int diskNumber,
        int partitionNumber,
        DriveLetterReassignmentResult reassignment)
    {
        ArgumentNullException.ThrowIfNull(reassignment);
        if (!reassignment.Changed)
            return null;

        string previousTargetRoot = ImagingPath.NormalizeDriveRoot(reassignment.PreviousTargetRoot);
        if (!TryGetDriveLetter(previousTargetRoot, out char previousTargetLetter) || previousTargetLetter == 'C')
            return null;

        bool hadDisplacedC = !string.IsNullOrWhiteSpace(reassignment.DisplacedCRoot);
        char displacedLetter = '\0';
        if (hadDisplacedC &&
            !TryGetDriveLetter(ImagingPath.NormalizeDriveRoot(reassignment.DisplacedCRoot), out displacedLetter))
        {
            return "The displaced C: drive letter could not be identified for rollback.";
        }

        string script =
            $"select disk {diskNumber}\r\n" +
            $"select partition {partitionNumber}\r\n" +
            "remove letter=C noerr\r\n";

        if (hadDisplacedC)
        {
            script +=
                $"select volume {displacedLetter}\r\n" +
                $"remove letter={displacedLetter} noerr\r\n" +
                "assign letter=C\r\n";
        }

        script +=
            $"select disk {diskNumber}\r\n" +
            $"select partition {partitionNumber}\r\n" +
            $"assign letter={previousTargetLetter}\r\n" +
            "exit\r\n";

        try
        {
            ProcessExecutionResult result = await DiskPartRunner.RunAsync(script, CancellationToken.None).ConfigureAwait(false);
            await WaitForDriveMountedAsync(previousTargetLetter, shouldExist: true, CancellationToken.None).ConfigureAwait(false);
            if (hadDisplacedC)
                await WaitForDriveMountedAsync('C', shouldExist: true, CancellationToken.None).ConfigureAwait(false);

            if (!result.Success || !IsDriveMounted(previousTargetLetter) || (hadDisplacedC && !IsDriveMounted('C')))
            {
                return BuildProcessFailure(
                    "DiskPart could not restore the pre-operation drive-letter assignments.",
                    result);
            }

            return null;
        }
        catch (Exception ex)
        {
            return "DiskPart could not restore the pre-operation drive-letter assignments.\n\n" + ex.Message;
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

    private static async Task WaitForDriveMountedAsync(
        char letter,
        bool shouldExist,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 15 && IsDriveMounted(letter) != shouldExist; attempt++)
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildProcessFailure(string message, ProcessExecutionResult result)
    {
        string detail = result.CombinedOutput;
        return string.IsNullOrWhiteSpace(detail) ? message : message + "\n\n" + detail;
    }
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
