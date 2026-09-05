using System.Diagnostics;
using System.Text;

namespace Imaging.Core;

public sealed class TemporaryDriveLetterService
{
    private static readonly object ReservationSync = new();
    private static readonly HashSet<char> ReservedLetters = new();

    public TemporaryDriveLetterResult Assign(int diskNumber, int partitionNumber)
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
        ProcessResult assign;
        try
        {
            assign = RunDiskPart(
                $"select disk {diskNumber}\r\n" +
                $"select partition {partitionNumber}\r\n" +
                $"assign letter={letter}\r\n" +
                "exit\r\n");
        }
        catch (Exception ex)
        {
            ReleaseReservation(letter);
            return TemporaryDriveLetterResult.Failed(ex.Message);
        }

        bool assigned = assign.ExitCode == 0;
        if (assigned)
            WaitForRootState(root, shouldExist: true);

        if (!assigned || !Directory.Exists(root))
        {
            string error = BuildProcessFailure(
                $"DiskPart could not make Disk {diskNumber}, Partition {partitionNumber} accessible as {letter}:.",
                assign);

            if (assigned)
            {
                string? cleanupError = Remove(diskNumber, partitionNumber, letter);
                if (!string.IsNullOrWhiteSpace(cleanupError))
                    error += "\n\n" + cleanupError;
            }

            if (!Directory.Exists(root))
                ReleaseReservation(letter);

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

    public string? Remove(TemporaryDriveLetterResult assignment)
    {
        if (!assignment.Success || assignment.DriveLetter == '\0')
            return null;

        return Remove(assignment.DiskNumber, assignment.PartitionNumber, assignment.DriveLetter);
    }

    private static string? Remove(int diskNumber, int partitionNumber, char letter)
    {
        string root = $"{letter}:\\";
        try
        {
            ProcessResult remove = RunDiskPart(
                $"select disk {diskNumber}\r\n" +
                $"select partition {partitionNumber}\r\n" +
                $"remove letter={letter}\r\n" +
                "exit\r\n");

            if (remove.ExitCode == 0)
                WaitForRootState(root, shouldExist: false);

            if (remove.ExitCode != 0 || Directory.Exists(root))
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

    private static ProcessResult RunDiskPart(string script)
    {
        string diskPartPath = Path.Combine(Environment.SystemDirectory, "diskpart.exe");
        if (!File.Exists(diskPartPath))
        {
            throw new FileNotFoundException(
                "DiskPart.exe was not found under the active Windows system directory.",
                diskPartPath);
        }

        string scriptPath = Path.Combine(Path.GetTempPath(), $"ImagingManager-{Guid.NewGuid():N}.txt");
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
            string output = outputTask.GetAwaiter().GetResult();
            string error = errorTask.GetAwaiter().GetResult();
            return new ProcessResult(process.ExitCode, output.Trim(), error.Trim());
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static void WaitForRootState(string root, bool shouldExist)
    {
        for (int attempt = 0; attempt < 10 && Directory.Exists(root) != shouldExist; attempt++)
            Thread.Sleep(100);
    }

    private static string BuildProcessFailure(string message, ProcessResult result)
    {
        string detail = string.Join(Environment.NewLine, new[] { result.StandardOutput, result.StandardError }
            .Where(static s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(detail) ? message : message + "\n\n" + detail;
    }

    private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);
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
