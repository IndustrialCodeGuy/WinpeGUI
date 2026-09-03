using System.Diagnostics;
using System.Text;

namespace Imaging.Core;

public sealed class TemporaryDriveLetterService
{
    public TemporaryDriveLetterResult Assign(int diskNumber, int partitionNumber)
    {
        char letter;
        try
        {
            letter = FindAvailableDriveLetter();
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
                return BuildProcessFailure(
                    $"DiskPart could not remove the temporary {letter}: drive letter from Disk {diskNumber}, Partition {partitionNumber}.",
                    remove);
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"DiskPart could not remove the temporary {letter}: drive letter from Disk {diskNumber}, Partition {partitionNumber}.\n\n{ex.Message}";
        }
    }

    private static char FindAvailableDriveLetter()
    {
        HashSet<char> used = Directory.GetLogicalDrives()
            .Where(static d => d.Length >= 2 && d[1] == ':')
            .Select(static d => char.ToUpperInvariant(d[0]))
            .ToHashSet();

        for (char letter = 'Z'; letter >= 'D'; letter--)
        {
            if (letter == 'X')
                continue;
            if (!used.Contains(letter))
                return letter;
        }

        throw new InvalidOperationException(
            "No unused drive letter is available for temporarily mounting the selected partition.");
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
