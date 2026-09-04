using BitLocker.Core;
using Shared.Shell.Utilities;
using System.Diagnostics;
using System.Management;
using System.Text;

namespace WinPEGui;

internal static class WinPeDriveLetterPolicy
{
    private const char PrimaryWindowsLetter = 'C';
    private static readonly char[] CandidateLetters = Enumerable.Range('D', 'Z' - 'D' + 1)
        .Select(static value => (char)value)
        .Reverse()
        .Where(static letter => letter != 'X')
        .ToArray();

    public static IReadOnlyList<string> NormalizePrimaryWindowsDrive(IEnumerable<string?> protectedPaths)
    {
        List<string> messages = new();

        if (!PlatformDetect.IsWinPE)
        {
            messages.Add("Drive-letter policy skipped because the launcher is not running in WinPE.");
            return messages;
        }

        try
        {
            Dictionary<string, PartitionLocation> partitionsByRoot = ReadPartitionLocations();
            Dictionary<int, ushort?> busTypesByDisk = ReadDiskBusTypes();
            Dictionary<int, string> pnpDeviceIdsByDisk = ReadDiskPnpDeviceIds();
            HashSet<string> protectedRoots = GetProtectedRoots(protectedPaths);
            List<WindowsVolumeCandidate> candidates = FindWindowsVolumeCandidates(
                partitionsByRoot,
                busTypesByDisk,
                pnpDeviceIdsByDisk,
                messages);

            WindowsVolumeCandidate? selected = SelectPrimaryCandidate(candidates, out string selectionReason);
            bool cLetterPresent = GetUsedDriveLetters().Contains(PrimaryWindowsLetter);
            PartitionLocation? currentC = partitionsByRoot.TryGetValue(@"C:\", out PartitionLocation? cLocation)
                ? cLocation
                : null;

            if (selected != null)
            {
                messages.Add(
                    $"Primary Windows candidate: {selected.Root} on Disk {selected.Location.DiskNumber}, " +
                    $"Partition {selected.Location.PartitionNumber} ({selectionReason}; usbAttached={FormatUsbAttached(selected.UsbAttached)}, " +
                    $"bus={FormatBusType(selected.BusType)}, driveType={selected.DriveType}).");

                if (selected.Root.Equals(@"C:\", StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add("C: already belongs to the selected primary Windows volume; no drive-letter change was needed.");
                    return messages;
                }

                if (protectedRoots.Contains(selected.Root))
                {
                    messages.Add(
                        $"C: normalization skipped because the selected Windows volume {selected.Root} hosts a protected launcher path.");
                    return messages;
                }

                if (cLetterPresent && currentC == null)
                {
                    messages.Add(
                        "C: normalization skipped because C: is assigned but could not be mapped to an MSFT_Partition. The existing assignment was preserved.");
                    return messages;
                }

                if (currentC != null && protectedRoots.Contains(@"C:\"))
                {
                    messages.Add(
                        "C: normalization skipped because the current C: volume hosts a protected launcher path and cannot be safely moved during startup.");
                    return messages;
                }

                HashSet<char> usedLetters = GetUsedDriveLetters();
                char replacementForC = '\0';

                if (currentC != null)
                {
                    replacementForC = FindAvailableDriveLetter(usedLetters);
                    if (replacementForC == '\0')
                    {
                        messages.Add("C: normalization skipped because no unused replacement drive letter is available for the current C: volume.");
                        return messages;
                    }
                }

                string script = BuildPrimaryAssignmentScript(currentC, replacementForC, selected);
                ProcessResult result = RunDiskPart(script);

                if (result.ExitCode != 0)
                {
                    messages.Add(BuildDiskPartFailure("C: normalization failed in DiskPart.", result));
                    return messages;
                }

                WaitForLetterTopologyChange();
                Dictionary<string, PartitionLocation> refreshed = ReadPartitionLocations();
                if (!refreshed.TryGetValue(@"C:\", out PartitionLocation? refreshedC) ||
                    refreshedC.DiskNumber != selected.Location.DiskNumber ||
                    refreshedC.PartitionNumber != selected.Location.PartitionNumber)
                {
                    messages.Add(
                        "DiskPart completed but C: did not resolve to the selected primary Windows partition. " +
                        "The resulting drive-letter layout was left unchanged for manual review.");
                    return messages;
                }

                if (currentC != null)
                {
                    messages.Add(
                        $"Moved the previous C: volume (Disk {currentC.DiskNumber}, Partition {currentC.PartitionNumber}) " +
                        $"to {replacementForC}: and assigned C: to {selected.Root.TrimEnd('\\')} " +
                        $"(Disk {selected.Location.DiskNumber}, Partition {selected.Location.PartitionNumber}).");
                }
                else
                {
                    messages.Add(
                        $"Assigned C: to the selected primary Windows volume on Disk {selected.Location.DiskNumber}, " +
                        $"Partition {selected.Location.PartitionNumber}.");
                }

                return messages;
            }

            messages.Add(selectionReason);

            if (currentC == null)
            {
                messages.Add(cLetterPresent
                    ? "C: is assigned but could not be mapped to an MSFT_Partition, so the existing assignment was preserved."
                    : "C: is already unassigned and remains reserved for a future primary Windows volume.");
                return messages;
            }

            if (candidates.Any(candidate => candidate.Root.Equals(@"C:\", StringComparison.OrdinalIgnoreCase)))
            {
                messages.Add(
                    "C: already belongs to a plausible Windows volume. Because the primary Windows volume is ambiguous, its current assignment was preserved.");
                return messages;
            }

            if (protectedRoots.Contains(@"C:\"))
            {
                messages.Add(
                    "C: could not be reserved because the current C: volume hosts a protected launcher path and cannot be safely moved during startup.");
                return messages;
            }

            HashSet<char> usedForReservation = GetUsedDriveLetters();
            char replacement = FindAvailableDriveLetter(usedForReservation);
            if (replacement == '\0')
            {
                messages.Add("C: could not be reserved because no unused replacement drive letter is available.");
                return messages;
            }

            ProcessResult reserveResult = RunDiskPart(BuildMoveLetterScript(currentC, PrimaryWindowsLetter, replacement));
            if (reserveResult.ExitCode != 0)
            {
                messages.Add(BuildDiskPartFailure("C: reservation failed in DiskPart.", reserveResult));
                return messages;
            }

            WaitForLetterTopologyChange();
            Dictionary<string, PartitionLocation> reservationRefresh = ReadPartitionLocations();
            if (reservationRefresh.ContainsKey(@"C:\"))
            {
                messages.Add(
                    "DiskPart completed but C: is still assigned. The drive-letter layout was left for manual review.");
                return messages;
            }

            messages.Add(
                $"No unambiguous primary Windows volume was selected. Moved the previous C: volume " +
                $"(Disk {currentC.DiskNumber}, Partition {currentC.PartitionNumber}) to {replacement}: so C: remains reserved.");
        }
        catch (Exception ex)
        {
            messages.Add($"Drive-letter policy failed safely without blocking shell startup: {ex.Message}");
        }

        return messages;
    }

    private static List<WindowsVolumeCandidate> FindWindowsVolumeCandidates(
        IReadOnlyDictionary<string, PartitionLocation> partitionsByRoot,
        IReadOnlyDictionary<int, ushort?> busTypesByDisk,
        IReadOnlyDictionary<int, string> pnpDeviceIdsByDisk,
        List<string> messages)
    {
        Dictionary<string, CandidateSignals> signalsByRoot = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            IReadOnlyList<BitLockerVolumeInfo> bitLockerVolumes = new BitLockerCompositeBackend().GetVolumes();
            foreach (BitLockerVolumeInfo volume in bitLockerVolumes)
            {
                if (!volume.IsSystemVolume)
                    continue;

                string root = NormalizeDriveRoot(volume.MountPoint);
                if (root.Length == 0 || root.Equals(@"X:\", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!signalsByRoot.TryGetValue(root, out CandidateSignals? signals))
                    signalsByRoot[root] = signals = new CandidateSignals();

                signals.BitLockerSystemVolume = true;
            }
        }
        catch (Exception ex)
        {
            messages.Add($"BitLocker OS-volume detection was unavailable: {ex.Message}");
        }

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            string root = NormalizeDriveRoot(drive.Name);
            if (root.Length == 0 || root.Equals(@"X:\", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!DriveSystemDetector.ContainsOfflineWindowsInstall(root))
                continue;

            if (!signalsByRoot.TryGetValue(root, out CandidateSignals? signals))
                signalsByRoot[root] = signals = new CandidateSignals();

            signals.ContainsOfflineWindows = true;
        }

        List<WindowsVolumeCandidate> candidates = new();
        foreach ((string root, CandidateSignals signals) in signalsByRoot)
        {
            if (!partitionsByRoot.TryGetValue(root, out PartitionLocation? location))
            {
                messages.Add($"Windows-volume candidate {root} could not be mapped to a physical disk/partition and was ignored.");
                continue;
            }

            busTypesByDisk.TryGetValue(location.DiskNumber, out ushort? busType);
            pnpDeviceIdsByDisk.TryGetValue(location.DiskNumber, out string? pnpDeviceId);

            bool? usbAttached = null;
            if (!string.IsNullOrWhiteSpace(pnpDeviceId) &&
                StorageDeviceTopology.TryIsUsbAttachedStorageDevice(pnpDeviceId, out bool resolvedUsbAttached))
            {
                usbAttached = resolvedUsbAttached;
            }

            DriveType driveType = GetDriveType(root);
            bool isFixedInternal = IsFixedInternalCandidate(driveType, busType, usbAttached);

            candidates.Add(new WindowsVolumeCandidate(
                root,
                location,
                signals.BitLockerSystemVolume,
                signals.ContainsOfflineWindows,
                isFixedInternal,
                usbAttached,
                busType,
                driveType));
        }

        List<WindowsVolumeCandidate> mergedCandidates = candidates
            .GroupBy(static candidate => (candidate.Location.DiskNumber, candidate.Location.PartitionNumber))
            .Select(static group =>
            {
                WindowsVolumeCandidate representative = group
                    .OrderByDescending(static candidate => candidate.Root.Equals(@"C:\", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(static candidate => candidate.Root, StringComparer.OrdinalIgnoreCase)
                    .First();

                return representative with
                {
                    BitLockerSystemVolume = group.Any(static candidate => candidate.BitLockerSystemVolume),
                    ContainsOfflineWindows = group.Any(static candidate => candidate.ContainsOfflineWindows)
                };
            })
            .OrderBy(static candidate => candidate.Location.DiskNumber)
            .ThenBy(static candidate => candidate.Location.PartitionNumber)
            .ToList();

        foreach (WindowsVolumeCandidate candidate in mergedCandidates)
        {
            string signals = string.Join("+", new[]
            {
                candidate.BitLockerSystemVolume ? "BitLocker-OS" : string.Empty,
                candidate.ContainsOfflineWindows ? "Windows-tree" : string.Empty
            }.Where(static value => value.Length > 0));

            messages.Add(
                $"Windows candidate {candidate.Root}: Disk {candidate.Location.DiskNumber}, Partition {candidate.Location.PartitionNumber}, " +
                $"signals={signals}, fixedInternal={candidate.IsFixedInternal}, usbAttached={FormatUsbAttached(candidate.UsbAttached)}, " +
                $"bus={FormatBusType(candidate.BusType)}, driveType={candidate.DriveType}.");
        }

        return mergedCandidates;
    }

    private static WindowsVolumeCandidate? SelectPrimaryCandidate(
        IReadOnlyList<WindowsVolumeCandidate> candidates,
        out string reason)
    {
        if (candidates.Count == 0)
        {
            reason = "No existing Windows system-volume candidate was detected at startup.";
            return null;
        }

        if (candidates.Count == 1)
        {
            reason = candidates[0].BitLockerSystemVolume
                ? "only BitLocker OS-volume candidate"
                : "only accessible Windows-installation candidate";
            return candidates[0];
        }

        WindowsVolumeCandidate[] fixedCandidates = candidates
            .Where(static candidate => candidate.IsFixedInternal)
            .ToArray();

        if (fixedCandidates.Length == 1)
        {
            reason = "multiple Windows system-volume candidates were found, but this was the only fixed/non-USB candidate";
            return fixedCandidates[0];
        }

        WindowsVolumeCandidate? currentC = candidates.FirstOrDefault(static candidate =>
            candidate.Root.Equals(@"C:\", StringComparison.OrdinalIgnoreCase));
        if (currentC != null)
        {
            reason = fixedCandidates.Length > 1
                ? "multiple fixed Windows candidates remain ambiguous; preserving the plausible candidate that already owns C:"
                : "multiple Windows candidates remain ambiguous; preserving the plausible candidate that already owns C:";
            return currentC;
        }

        reason = fixedCandidates.Length > 1
            ? $"Multiple Windows system-volume candidates were detected and {fixedCandidates.Length} are fixed/non-USB; no automatic C: target was chosen."
            : "Multiple Windows system-volume candidates were detected and no unique fixed/non-USB candidate exists; no automatic C: target was chosen.";
        return null;
    }

    private static bool IsFixedInternalCandidate(DriveType driveType, ushort? busType, bool? usbAttached)
    {
        // Prefer the same physical PnP ancestry test File Manager uses to decide
        // whether a storage device is ejectable. SATA/NVMe-to-USB bridges often
        // surface the logical volume as Fixed and can report a non-USB storage bus,
        // but their parent chain still passes through USBSTOR/UASPSTOR/USB.
        if (usbAttached == true)
            return false;

        // If PnP ancestry was unavailable, keep BusType as a conservative fallback
        // before trusting DriveType.Fixed, since USB bridge volumes often report Fixed.
        if (usbAttached == null && busType is 4 or 7) // IEEE 1394 or USB
            return false;

        if (driveType == DriveType.Removable)
            return false;

        if (driveType == DriveType.Fixed)
            return true;

        // A locked volume can occasionally report an unhelpful logical drive type.
        // A resolved non-USB ancestry plus a normal storage bus is enough to keep an
        // installed disk in the fixed/internal tie-breaker. SD/MMC are intentionally
        // not rejected here; embedded storage can legitimately host Windows.
        return busType is 1 or 2 or 3 or 5 or 6 or 8 or 9 or 10 or 11 or 14 or 16 or 17 or 18 or 19;
    }

    private static Dictionary<string, PartitionLocation> ReadPartitionLocations()
    {
        Dictionary<string, PartitionLocation> result = new(StringComparer.OrdinalIgnoreCase);

        using ManagementObjectSearcher searcher = new(
            @"root\Microsoft\Windows\Storage",
            "SELECT DiskNumber, PartitionNumber, DriveLetter, AccessPaths FROM MSFT_Partition");
        using ManagementObjectCollection partitions = searcher.Get();

        foreach (ManagementObject partition in partitions.Cast<ManagementObject>())
        {
            using (partition)
            {
                int diskNumber = ReadInt32(partition["DiskNumber"]);
                int partitionNumber = ReadInt32(partition["PartitionNumber"]);
                PartitionLocation location = new(diskNumber, partitionNumber);

                string driveLetter = ReadDriveLetter(partition["DriveLetter"]);
                if (driveLetter.Length > 0)
                    result[driveLetter + "\\"] = location;

                if (partition["AccessPaths"] is string[] accessPaths)
                {
                    foreach (string accessPath in accessPaths)
                    {
                        string root = NormalizeDriveRoot(accessPath);
                        if (root.Length > 0 && root.Length == 3 && root[1] == ':')
                            result[root] = location;
                    }
                }
            }
        }

        return result;
    }

    private static Dictionary<int, ushort?> ReadDiskBusTypes()
    {
        Dictionary<int, ushort?> result = new();

        using ManagementObjectSearcher searcher = new(
            @"root\Microsoft\Windows\Storage",
            "SELECT Number, BusType FROM MSFT_Disk");
        using ManagementObjectCollection disks = searcher.Get();

        foreach (ManagementObject disk in disks.Cast<ManagementObject>())
        {
            using (disk)
            {
                int diskNumber = ReadInt32(disk["Number"]);
                result[diskNumber] = ReadNullableUInt16(disk["BusType"]);
            }
        }

        return result;
    }

    private static Dictionary<int, string> ReadDiskPnpDeviceIds()
    {
        Dictionary<int, string> result = new();

        try
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT Index, PNPDeviceID FROM Win32_DiskDrive");
            using ManagementObjectCollection disks = searcher.Get();

            foreach (ManagementObject disk in disks.Cast<ManagementObject>())
            {
                using (disk)
                {
                    int diskNumber = ReadInt32(disk["Index"]);
                    string pnpDeviceId = Convert.ToString(disk["PNPDeviceID"])?.Trim() ?? string.Empty;
                    if (pnpDeviceId.Length > 0)
                        result[diskNumber] = pnpDeviceId;
                }
            }
        }
        catch
        {
            // BusType remains available as a fallback when the legacy WMI mapping
            // is unavailable in a particular PE image.
        }

        return result;
    }

    private static HashSet<string> GetProtectedRoots(IEnumerable<string?> paths)
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            string root = NormalizeDriveRoot(path);
            if (root.Length > 0)
                roots.Add(root);
        }

        return roots;
    }

    private static HashSet<char> GetUsedDriveLetters()
    {
        return Directory.GetLogicalDrives()
            .Where(static root => root.Length >= 2 && root[1] == ':')
            .Select(static root => char.ToUpperInvariant(root[0]))
            .ToHashSet();
    }

    private static char FindAvailableDriveLetter(IReadOnlySet<char> usedLetters)
    {
        foreach (char letter in CandidateLetters)
        {
            if (!usedLetters.Contains(letter))
                return letter;
        }

        return '\0';
    }

    private static string BuildPrimaryAssignmentScript(
        PartitionLocation? currentC,
        char replacementForC,
        WindowsVolumeCandidate selected)
    {
        StringBuilder script = new();

        if (currentC != null)
        {
            AppendSelectPartition(script, currentC);
            script.AppendLine($"remove letter={PrimaryWindowsLetter}");
            script.AppendLine($"assign letter={replacementForC}");
        }

        char selectedLetter = char.ToUpperInvariant(selected.Root[0]);
        AppendSelectPartition(script, selected.Location);
        if (selectedLetter != PrimaryWindowsLetter)
            script.AppendLine($"remove letter={selectedLetter}");
        script.AppendLine($"assign letter={PrimaryWindowsLetter}");
        script.AppendLine("exit");
        return script.ToString();
    }

    private static string BuildMoveLetterScript(PartitionLocation location, char fromLetter, char toLetter)
    {
        StringBuilder script = new();
        AppendSelectPartition(script, location);
        script.AppendLine($"remove letter={fromLetter}");
        script.AppendLine($"assign letter={toLetter}");
        script.AppendLine("exit");
        return script.ToString();
    }

    private static void AppendSelectPartition(StringBuilder script, PartitionLocation location)
    {
        script.AppendLine($"select disk {location.DiskNumber}");
        script.AppendLine($"select partition {location.PartitionNumber}");
    }

    private static ProcessResult RunDiskPart(string script)
    {
        string diskPartPath = Path.Combine(Environment.SystemDirectory, "diskpart.exe");
        if (!File.Exists(diskPartPath))
            throw new FileNotFoundException("DiskPart.exe was not found under the active Windows system directory.", diskPartPath);

        string scriptPath = Path.Combine(Path.GetTempPath(), $"WinPEGui-DriveLetters-{Guid.NewGuid():N}.txt");
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
            return new ProcessResult(
                process.ExitCode,
                outputTask.GetAwaiter().GetResult().Trim(),
                errorTask.GetAwaiter().GetResult().Trim());
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static void WaitForLetterTopologyChange()
    {
        // Mount Manager normally updates immediately, but give storage/WMI a short
        // chance to settle before the shell processes enumerate drives.
        Thread.Sleep(250);
    }

    private static string BuildDiskPartFailure(string message, ProcessResult result)
    {
        string detail = string.Join(Environment.NewLine, new[] { result.StandardOutput, result.StandardError }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(detail) ? message : message + " " + detail.Replace(Environment.NewLine, " | ");
    }

    private static string NormalizeDriveRoot(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string? root = Path.GetPathRoot(path.Trim());
            if (string.IsNullOrWhiteSpace(root) || root.Length < 2 || root[1] != ':')
                return string.Empty;

            return char.ToUpperInvariant(root[0]) + @":\";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DriveType GetDriveType(string root)
    {
        try { return new DriveInfo(root).DriveType; }
        catch { return DriveType.Unknown; }
    }

    private static string ReadDriveLetter(object? value)
    {
        if (value is char character && character != '\0')
            return char.ToUpperInvariant(character) + ":";

        try
        {
            if (value is ushort or short or uint or int)
            {
                char numericCharacter = Convert.ToChar(value);
                if (numericCharacter != '\0' && char.IsLetter(numericCharacter))
                    return char.ToUpperInvariant(numericCharacter) + ":";
            }
        }
        catch
        {
        }

        string text = Convert.ToString(value)?.Trim().TrimEnd(':') ?? string.Empty;
        return text.Length == 1 && char.IsLetter(text[0])
            ? char.ToUpperInvariant(text[0]) + ":"
            : string.Empty;
    }

    private static int ReadInt32(object? value)
    {
        try { return value == null ? 0 : Convert.ToInt32(value); }
        catch { return 0; }
    }

    private static ushort? ReadNullableUInt16(object? value)
    {
        try { return value == null ? null : Convert.ToUInt16(value); }
        catch { return null; }
    }

    private static string FormatUsbAttached(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        null => "Unknown"
    };

    private static string FormatBusType(ushort? value) => value switch
    {
        null => "Unknown",
        0 => "Unknown",
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE 1394",
        5 => "SSA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "File-backed virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => $"Unknown ({value})"
    };

    private sealed class CandidateSignals
    {
        public bool BitLockerSystemVolume { get; set; }
        public bool ContainsOfflineWindows { get; set; }
    }

    private sealed record PartitionLocation(int DiskNumber, int PartitionNumber);

    private sealed record WindowsVolumeCandidate(
        string Root,
        PartitionLocation Location,
        bool BitLockerSystemVolume,
        bool ContainsOfflineWindows,
        bool IsFixedInternal,
        bool? UsbAttached,
        ushort? BusType,
        DriveType DriveType);

    private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
