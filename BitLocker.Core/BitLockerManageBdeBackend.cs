using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BitLocker.Core;

public sealed class BitLockerManageBdeBackend
{
    // The status parser intentionally consumes manage-bde output as blocks. This
    // keeps the UI status pane close to the command-line text while extracting
    // only the fields needed for drive state and icons.
    private static readonly Regex VolumeHeaderRegex = new(
        @"^Volume\s+([A-Z]:)\s*(?:\[(.*)\])?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Lazy<string> ManageBdePath = new(ResolveManageBdePath);

    public IReadOnlyList<BitLockerVolumeInfo> GetVolumes()
    {
        (int exitCode, string stdOut, string stdErr) = RunManageBde("-status");

        if (exitCode != 0)
        {
            string message = BuildFailureMessage(stdOut, stdErr, "manage-bde -status failed.");
            throw new InvalidOperationException(message);
        }

        return ParseStatusOutput(stdOut)
            .OrderBy(static v => v.MountPoint, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public BitLockerOperationResult UnlockWithRecoveryKeyFile(string mountPoint, string keyFilePath)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
            return BitLockerOperationResult.Fail(1, "A drive must be selected.");

        if (string.IsNullOrWhiteSpace(keyFilePath) || !File.Exists(keyFilePath))
            return BitLockerOperationResult.Fail(1, "The recovery key file was not found.");

        try
        {
            (int exitCode, string stdOut, string stdErr) = RunManageBde(
                $"-unlock {QuoteArg(mountPoint)} -recoverykey {QuoteArg(keyFilePath)}");

            if (exitCode == 0)
                return BitLockerOperationResult.Ok("The drive was unlocked successfully.");

            string message = BuildFailureMessage(stdOut, stdErr, "The drive could not be unlocked with the recovery key file.");
            return BitLockerOperationResult.Fail((uint)exitCode, message);
        }
        catch (Exception ex)
        {
            return BitLockerOperationResult.Fail(1, ex.Message);
        }
    }

    private static IReadOnlyList<BitLockerVolumeInfo> ParseStatusOutput(string output)
    {
        List<BitLockerVolumeInfo> volumes = new();
        List<string> currentBlock = new();

        foreach (string rawLine in ReadLines(output))
        {
            Match headerMatch = VolumeHeaderRegex.Match(rawLine);
            if (headerMatch.Success)
            {
                AddVolumeIfPresent(volumes, currentBlock);
                currentBlock.Clear();
            }

            if (currentBlock.Count > 0 || headerMatch.Success)
                currentBlock.Add(rawLine.TrimEnd());
        }

        AddVolumeIfPresent(volumes, currentBlock);
        return volumes;
    }

    private static void AddVolumeIfPresent(List<BitLockerVolumeInfo> volumes, List<string> blockLines)
    {
        while (blockLines.Count > 0 && string.IsNullOrWhiteSpace(blockLines[^1]))
            blockLines.RemoveAt(blockLines.Count - 1);

        if (blockLines.Count == 0)
            return;

        Match headerMatch = VolumeHeaderRegex.Match(blockLines[0]);
        if (!headerMatch.Success)
            return;

        string mountPoint = NormalizeMountPoint(headerMatch.Groups[1].Value);
        string label = headerMatch.Groups[2].Success
            ? headerMatch.Groups[2].Value.Trim()
            : string.Empty;

        string lockStatus = GetStatusField(blockLines, "Lock Status");
        string protectionStatus = GetStatusField(blockLines, "Protection Status");
        string encryptionMethod = GetStatusField(blockLines, "Encryption Method");

        BitLockerLockState lockState = ParseLockState(lockStatus);
        BitLockerEncryptionState encryptionState = ParseEncryptionMethod(encryptionMethod);
        BitLockerProtectionState protectionState = ParseProtectionState(protectionStatus);
        BitLockerKeyProtectorState keyProtectorState = ParseKeyProtectorState(blockLines);
        BitLockerResolvedState resolvedState = BitLockerVolumeStateResolver.Resolve(new BitLockerStateInput(
            lockState,
            encryptionState,
            protectionState,
            keyProtectorState));

        bool isSystemVolume = blockLines.Any(static line =>
            line.IndexOf("[OS Volume]", StringComparison.OrdinalIgnoreCase) >= 0);

        volumes.Add(new BitLockerVolumeInfo
        {
            MountPoint = mountPoint,
            VolumeLabel = label,
            IsLocked = ToNullableBool(lockState),
            IsStatusKnown = resolvedState.IsStatusKnown,
            LockStatusText = string.IsNullOrWhiteSpace(lockStatus) ? "Unknown" : lockStatus,
            ProtectionOn = resolvedState.ProtectionOn,
            ProtectionOff = resolvedState.ProtectionOff,
            IsEncrypted = resolvedState.IsEncrypted,
            HasKeyProtectors = resolvedState.HasKeyProtectors,
            IsBitLockerCapable = resolvedState.IsBitLockerVolume,
            VisualState = resolvedState.VisualState,
            IsSystemVolume = isSystemVolume,
            VolumeTypeText = GetVolumeTypeText(mountPoint, isSystemVolume),
            RecoveryKeyId = string.Empty,
            ProtectorSummary = Array.Empty<string>(),
            StatusText = string.Join(Environment.NewLine, blockLines)
        });
    }

    private static BitLockerLockState ParseLockState(string lockStatus)
    {
        if (lockStatus.Equals("Locked", StringComparison.OrdinalIgnoreCase))
            return BitLockerLockState.Locked;

        if (lockStatus.Equals("Unlocked", StringComparison.OrdinalIgnoreCase))
            return BitLockerLockState.Unlocked;

        return BitLockerLockState.Unknown;
    }

    private static BitLockerProtectionState ParseProtectionState(string protectionStatus)
    {
        if (protectionStatus.Equals("Protection On", StringComparison.OrdinalIgnoreCase))
            return BitLockerProtectionState.On;

        if (protectionStatus.Equals("Protection Off", StringComparison.OrdinalIgnoreCase))
            return BitLockerProtectionState.Off;

        return BitLockerProtectionState.Unknown;
    }

    private static BitLockerEncryptionState ParseEncryptionMethod(string encryptionMethod)
    {
        if (string.IsNullOrWhiteSpace(encryptionMethod) ||
            encryptionMethod.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return BitLockerEncryptionState.Unknown;
        }

        if (encryptionMethod.Equals("None", StringComparison.OrdinalIgnoreCase))
            return BitLockerEncryptionState.NotEncrypted;

        return BitLockerEncryptionState.Encrypted;
    }

    private static BitLockerKeyProtectorState ParseKeyProtectorState(List<string> blockLines)
    {
        const string keyProtectorsPrefix = "Key Protectors:";

        for (int index = 0; index < blockLines.Count; index++)
        {
            string trimmed = blockLines[index].Trim();
            if (!trimmed.StartsWith(keyProtectorsPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string inlineValue = trimmed[keyProtectorsPrefix.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(inlineValue))
                return ParseKeyProtectorEntry(inlineValue);

            for (int protectorIndex = index + 1; protectorIndex < blockLines.Count; protectorIndex++)
            {
                string protectorText = blockLines[protectorIndex].Trim();
                if (string.IsNullOrWhiteSpace(protectorText))
                    continue;

                return ParseKeyProtectorEntry(protectorText);
            }

            return BitLockerKeyProtectorState.Unknown;
        }

        return BitLockerKeyProtectorState.Unknown;
    }

    private static BitLockerKeyProtectorState ParseKeyProtectorEntry(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("None Found", StringComparison.OrdinalIgnoreCase))
        {
            return BitLockerKeyProtectorState.None;
        }

        if (value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return BitLockerKeyProtectorState.Unknown;

        return BitLockerKeyProtectorState.Present;
    }

    private static IEnumerable<string> ReadLines(string value)
    {
        using StringReader reader = new(value ?? string.Empty);

        string? line;
        while ((line = reader.ReadLine()) != null)
            yield return line;
    }

    private static string GetStatusField(List<string> blockLines, string fieldName)
    {
        string prefix = fieldName + ":";

        foreach (string line in blockLines)
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            return trimmed[prefix.Length..].Trim();
        }

        return string.Empty;
    }

    private static bool? ToNullableBool(BitLockerLockState lockState)
    {
        return lockState switch
        {
            BitLockerLockState.Locked => true,
            BitLockerLockState.Unlocked => false,
            _ => null
        };
    }

    private static string GetVolumeTypeText(string mountPoint, bool isSystemVolume)
    {
        try
        {
            DriveInfo drive = new(mountPoint);
            if (drive.DriveType == DriveType.Removable)
                return "Removable";
        }
        catch
        {
        }

        return isSystemVolume ? "System" : "Fixed";
    }

    private static string ResolveManageBdePath()
    {
        string? systemRoot = Environment.GetEnvironmentVariable("SystemRoot");

        List<string> candidates = new();

        if (!string.IsNullOrWhiteSpace(Environment.SystemDirectory))
            candidates.Add(Path.Combine(Environment.SystemDirectory, "manage-bde.exe"));

        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            candidates.Add(Path.Combine(systemRoot, "Sysnative", "manage-bde.exe"));
            candidates.Add(Path.Combine(systemRoot, "System32", "manage-bde.exe"));
        }

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "manage-bde.exe was not found. Expected it under the active Windows System32 directory.",
            "manage-bde.exe");
    }

    // Read both streams asynchronously before collecting the exit code so a
    // verbose manage-bde failure cannot deadlock on a full redirected pipe.
    private static (int ExitCode, string StdOut, string StdErr) RunManageBde(string arguments)
    {
        string manageBdePath = ManageBdePath.Value;

        ProcessStartInfo psi = new()
        {
            FileName = manageBdePath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(manageBdePath) ?? Environment.SystemDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Unable to start manage-bde.exe.");

        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        string stdOut = stdOutTask.GetAwaiter().GetResult();
        string stdErr = stdErrTask.GetAwaiter().GetResult();

        return (process.ExitCode, stdOut, stdErr);
    }

    private static string BuildFailureMessage(string stdOut, string stdErr, string fallback)
    {
        string message = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;
        message = (message ?? string.Empty).Trim();

        return string.IsNullOrWhiteSpace(message) ? fallback : message;
    }

    private static string NormalizeMountPoint(string value)
    {
        string trimmed = value.Trim();

        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            return trimmed + @"\";

        return trimmed;
    }

    private static string QuoteArg(string value)
    {
        return $"\"{value}\"";
    }
}
