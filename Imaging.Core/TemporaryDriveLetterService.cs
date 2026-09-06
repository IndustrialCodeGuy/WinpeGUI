namespace Imaging.Core;

public sealed class TemporaryDriveLetterService
{
    private static readonly object ReservationSync = new();
    private static readonly HashSet<char> ReservedLetters = new();

    public async Task<TemporaryDriveLetterResult> AssignAsync(
        int diskNumber,
        int partitionNumber,
        CancellationToken cancellationToken)
    {
        char letter;
        try
        {
            letter = ReserveAvailable().DriveLetter;
        }
        catch (Exception ex)
        {
            return TemporaryDriveLetterResult.Failed(ex.Message);
        }

        string root = $"{letter}:\\";
        ProcessExecutionResult assign;
        try
        {
            assign = await DiskPartRunner.RunAsync(
                $"select disk {diskNumber}\r\n" +
                $"select partition {partitionNumber}\r\n" +
                $"assign letter={letter}\r\n" +
                "exit\r\n",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ReleaseReservation(letter);
            throw;
        }
        catch (Exception ex)
        {
            ReleaseReservation(letter);
            return TemporaryDriveLetterResult.Failed(ex.Message);
        }

        if (assign.ExitCode == 0)
        {
            try
            {
                await WaitForRootStateAsync(root, shouldExist: true, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (Directory.Exists(root))
                {
                    _ = await RemoveAsync(
                        diskNumber,
                        partitionNumber,
                        letter,
                        CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    ReleaseReservation(letter);
                }
                throw;
            }
        }

        if (!assign.Success || !Directory.Exists(root))
        {
            string error = BuildProcessFailure(
                $"DiskPart could not make Disk {diskNumber}, Partition {partitionNumber} accessible as {letter}:.",
                assign);

            if (Directory.Exists(root))
            {
                string? cleanupError = await RemoveAsync(
                    diskNumber,
                    partitionNumber,
                    letter,
                    CancellationToken.None).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(cleanupError))
                    error += "\n\n" + cleanupError;
            }
            else
            {
                ReleaseReservation(letter);
            }

            return TemporaryDriveLetterResult.Failed(error);
        }

        return new TemporaryDriveLetterResult
        {
            Success = true,
            DiskNumber = diskNumber,
            PartitionNumber = partitionNumber,
            DriveLetter = letter,
            Root = root
        };
    }

    public TemporaryDriveLetterReservation ReserveAvailable(params char[] excludedLetters)
    {
        HashSet<char> excluded = excludedLetters
            .Select(char.ToUpperInvariant)
            .Where(static letter => letter is >= 'A' and <= 'Z')
            .ToHashSet();

        char letter = FindAndReserveAvailableDriveLetter(preferHigh: true, excluded);
        return new TemporaryDriveLetterReservation(letter);
    }

    public TemporaryDriveLetterReservation ReserveLowestAvailable(params char[] excludedLetters)
    {
        HashSet<char> excluded = excludedLetters
            .Select(char.ToUpperInvariant)
            .Where(static letter => letter is >= 'A' and <= 'Z')
            .ToHashSet();

        char letter = FindAndReserveAvailableDriveLetter(preferHigh: false, excluded);
        return new TemporaryDriveLetterReservation(letter);
    }

    public void Release(TemporaryDriveLetterReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ReleaseReservation(reservation.DriveLetter);
    }

    public void ReleaseReservation(TemporaryDriveLetterResult assignment)
    {
        if (assignment.Success)
            ReleaseReservation(assignment.DriveLetter);
    }

    public Task<string?> RemoveAsync(
        TemporaryDriveLetterResult assignment,
        CancellationToken cancellationToken)
    {
        if (!assignment.Success || assignment.DriveLetter == '\0')
            return Task.FromResult<string?>(null);

        return RemoveAsync(
            assignment.DiskNumber,
            assignment.PartitionNumber,
            assignment.DriveLetter,
            cancellationToken);
    }

    private static async Task<string?> RemoveAsync(
        int diskNumber,
        int partitionNumber,
        char letter,
        CancellationToken cancellationToken)
    {
        string root = $"{letter}:\\";
        try
        {
            ProcessExecutionResult remove = await DiskPartRunner.RunAsync(
                $"select disk {diskNumber}\r\n" +
                $"select partition {partitionNumber}\r\n" +
                $"remove letter={letter}\r\n" +
                "exit\r\n",
                cancellationToken).ConfigureAwait(false);

            if (remove.Success)
                await WaitForRootStateAsync(root, shouldExist: false, CancellationToken.None).ConfigureAwait(false);

            if (!remove.Success || Directory.Exists(root))
            {
                if (!Directory.Exists(root))
                    ReleaseReservation(letter);

                return BuildProcessFailure(
                    $"DiskPart could not remove the temporary {letter}: drive letter from Disk {diskNumber}, Partition {partitionNumber}.",
                    remove);
            }

            ReleaseReservation(letter);
            return null;
        }
        catch (OperationCanceledException)
        {
            if (!Directory.Exists(root))
                ReleaseReservation(letter);
            throw;
        }
        catch (Exception ex)
        {
            if (!Directory.Exists(root))
                ReleaseReservation(letter);

            return $"DiskPart could not remove the temporary {letter}: drive letter from Disk {diskNumber}, Partition {partitionNumber}.\n\n{ex.Message}";
        }
    }

    private static char FindAndReserveAvailableDriveLetter(bool preferHigh, HashSet<char> excluded)
    {
        lock (ReservationSync)
        {
            HashSet<char> used = Directory.GetLogicalDrives()
                .Where(static d => d.Length >= 2 && d[1] == ':')
                .Select(static d => char.ToUpperInvariant(d[0]))
                .ToHashSet();

            foreach (char reserved in ReservedLetters)
                used.Add(reserved);
            foreach (char excludedLetter in excluded)
                used.Add(excludedLetter);

            if (preferHigh)
            {
                for (char letter = 'Z'; letter >= 'D'; letter--)
                {
                    if (letter == 'X' || used.Contains(letter))
                        continue;

                    ReservedLetters.Add(letter);
                    return letter;
                }
            }
            else
            {
                for (char letter = 'D'; letter <= 'Z'; letter++)
                {
                    if (letter == 'X' || used.Contains(letter))
                        continue;

                    ReservedLetters.Add(letter);
                    return letter;
                }
            }
        }

        throw new InvalidOperationException(
            "No unused drive letter is available for this operation.");
    }

    private static void ReleaseReservation(char letter)
    {
        if (letter == '\0')
            return;

        lock (ReservationSync)
            ReservedLetters.Remove(char.ToUpperInvariant(letter));
    }

    private static async Task WaitForRootStateAsync(
        string root,
        bool shouldExist,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 10 && Directory.Exists(root) != shouldExist; attempt++)
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildProcessFailure(string message, ProcessExecutionResult result)
    {
        string detail = result.CombinedOutput;
        return string.IsNullOrWhiteSpace(detail) ? message : message + "\n\n" + detail;
    }
}

public sealed class TemporaryDriveLetterReservation
{
    internal TemporaryDriveLetterReservation(char driveLetter)
    {
        DriveLetter = char.ToUpperInvariant(driveLetter);
    }

    public char DriveLetter { get; }
    public string Root => $"{DriveLetter}:\\";
}

public sealed class TemporaryDriveLetterResult
{
    public bool Success { get; init; }
    public int DiskNumber { get; init; }
    public int PartitionNumber { get; init; }
    public char DriveLetter { get; init; }
    public string Root { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;

    public static TemporaryDriveLetterResult Failed(string error) => new()
    {
        Success = false,
        Error = error
    };
}
