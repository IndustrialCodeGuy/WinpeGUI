using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Imaging.Core;

public sealed class WinReStagingService
{
    private static readonly Regex RecoveryLocationRegex = new(
        @"(?<location>\\\\\?\\GLOBALROOT\\device\\harddisk(?<disk>\d+)\\partition(?<partition>\d+)(?<relative>\\[^\r\n]*))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string GetWindowsDirectory(string sourceRoot)
    {
        string root = ImagingPath.NormalizeDriveRoot(sourceRoot);
        return Path.Combine(root, "Windows");
    }

    public string GetWinRePath(string sourceRoot) =>
        Path.Combine(GetWindowsDirectory(sourceRoot), "System32", "Recovery", "winre.wim");

    public bool IsWindowsInstallation(string sourceRoot) =>
        Directory.Exists(Path.Combine(GetWindowsDirectory(sourceRoot), "System32"));

    public WinReStageResult StageFromConfiguredRecoveryPartition(string sourceRoot)
    {
        string root = ImagingPath.NormalizeDriveRoot(sourceRoot);
        if (root.Length == 0)
            return WinReStageResult.Failed("The selected partition does not have a usable drive letter.");

        string destination = GetWinRePath(root);
        if (File.Exists(destination))
            return WinReStageResult.AlreadyPresent(destination);

        RecoveryLocationResult location = FindConfiguredRecoveryLocation(root);
        if (!location.Success)
            return WinReStageResult.Failed(location.Error);

        char temporaryLetter;
        try
        {
            temporaryLetter = FindAvailableDriveLetter();
        }
        catch (Exception ex)
        {
            return WinReStageResult.Failed(ex.Message);
        }

        string tempRoot = $"{temporaryLetter}:\\";
        bool assigned = false;
        bool copied = false;
        string stagingError = string.Empty;
        string? removalError = null;

        try
        {
            ProcessResult assign = RunDiskPart(
                $"select disk {location.DiskNumber}\r\n" +
                $"select partition {location.PartitionNumber}\r\n" +
                $"assign letter={temporaryLetter}\r\n" +
                "exit\r\n");

            // If DiskPart reports success, always remember that the letter may
            // now exist so the finally block can attempt to remove it even if
            // the volume never becomes accessible or winre.wim is missing.
            assigned = assign.ExitCode == 0;

            if (assigned && !Directory.Exists(tempRoot))
                Thread.Sleep(150);

            if (!assigned || !Directory.Exists(tempRoot))
            {
                stagingError = BuildProcessFailure(
                    "DiskPart could not temporarily assign an accessible drive letter to the configured Windows RE partition.",
                    assign);
            }
            else
            {
                string relativeDirectory = NormalizeRecoveryRelativePath(location.RelativePath);
                string source = Path.Combine(tempRoot, relativeDirectory.TrimStart('\\'), "winre.wim");
                if (!File.Exists(source))
                {
                    stagingError =
                        $"The configured Windows RE partition was mounted as {temporaryLetter}:, but winre.wim was not found at:\n\n{source}";
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination, overwrite: false);
                    copied = true;
                }
            }
        }
        catch (Exception ex)
        {
            stagingError = ex.Message;
        }
        finally
        {
            if (assigned)
            {
                try
                {
                    ProcessResult remove = RunDiskPart(
                        $"select disk {location.DiskNumber}\r\n" +
                        $"select partition {location.PartitionNumber}\r\n" +
                        $"remove letter={temporaryLetter}\r\n" +
                        "exit\r\n");

                    if (remove.ExitCode == 0 && Directory.Exists(tempRoot))
                        Thread.Sleep(150);

                    if (remove.ExitCode != 0 || Directory.Exists(tempRoot))
                    {
                        removalError = BuildProcessFailure(
                            $"DiskPart could not remove the temporary {temporaryLetter}: drive letter.",
                            remove);
                    }
                }
                catch (Exception ex)
                {
                    removalError = $"DiskPart could not remove the temporary {temporaryLetter}: drive letter.\n\n{ex.Message}";
                }
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

    public void RemoveStagedWinRe(string sourceRoot)
    {
        string path = GetWinRePath(sourceRoot);
        if (File.Exists(path))
            File.Delete(path);
    }

    private RecoveryLocationResult FindConfiguredRecoveryLocation(string sourceRoot)
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

        ProcessStartInfo startInfo = new()
        {
            FileName = reagentc,
            WorkingDirectory = Path.GetDirectoryName(reagentc) ?? windowsDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/info");
        startInfo.ArgumentList.Add("/target");
        startInfo.ArgumentList.Add(windowsDirectory);

        ProcessResult result = RunProcess(startInfo);
        string combined = string.Join(Environment.NewLine, new[] { result.StandardOutput, result.StandardError }
            .Where(static s => !string.IsNullOrWhiteSpace(s)));

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

    private static string NormalizeRecoveryRelativePath(string relativePath)
    {
        string path = (relativePath ?? string.Empty).Trim();
        if (path.Length == 0 || path == "\\")
            return @"\Recovery\WindowsRE";

        return path.TrimEnd('\\');
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
            "No unused drive letter is available for temporarily mounting the Windows RE partition.");
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
            return RunProcess(startInfo);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static ProcessResult RunProcess(ProcessStartInfo startInfo)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {Path.GetFileName(startInfo.FileName)}.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output.Trim(), error.Trim());
    }

    private static string BuildProcessFailure(string message, ProcessResult result)
    {
        string detail = string.Join(Environment.NewLine, new[] { result.StandardOutput, result.StandardError }
            .Where(static s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(detail) ? message : message + "\n\n" + detail;
    }

    private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);

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
