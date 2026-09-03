using BitLocker.Core;
using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;

namespace Imaging.Core;

public sealed class DiskInventory
{
    private static readonly Regex PercentageRegex = new(@"(?<value>\d{1,3}(?:\.\d+)?)\s*%", RegexOptions.Compiled);

    public IReadOnlyList<ImagingDiskInfo> GetDisks()
    {
        BitLockerStatusSnapshot bitLockerStatus = GetBitLockerVolumesBestEffort();
        IReadOnlyList<BitLockerVolumeInfo> bitLockerVolumes = bitLockerStatus.Volumes;

        Dictionary<int, ImagingDiskStorageInfo> storageDisks = GetStorageDisksBestEffort(out string storageDiskError);
        Dictionary<(int DiskNumber, ulong OffsetBytes), ImagingPartitionStorageInfo> storagePartitions =
            GetStoragePartitionsBestEffort(out string storagePartitionError);
        Dictionary<int, List<ImagingPartitionInfo>> partitionsByDisk = GetPartitions(storagePartitions);
        List<ImagingDiskInfo> disks = new();

        using ManagementObjectSearcher searcher = new(
            @"root\CIMV2",
            "SELECT Index, DeviceID, Model, InterfaceType, MediaType, SerialNumber, Size FROM Win32_DiskDrive");
        using ManagementObjectCollection results = searcher.Get();

        foreach (ManagementObject disk in results.Cast<ManagementObject>())
        {
            using (disk)
            {
                int diskNumber = ReadInt32(disk["Index"]);
                partitionsByDisk.TryGetValue(diskNumber, out List<ImagingPartitionInfo>? partitions);
                partitions ??= new List<ImagingPartitionInfo>();
                storageDisks.TryGetValue(diskNumber, out ImagingDiskStorageInfo? storageInfo);

                string[] driveLetters = partitions
                    .SelectMany(static p => p.DriveLetters)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                ImagingBitLockerVolumeInfo[] diskBitLockerVolumes = bitLockerVolumes
                    .Where(v => driveLetters.Any(d =>
                        string.Equals(
                            ImagingPath.NormalizeDriveRoot(v.MountPoint),
                            ImagingPath.NormalizeDriveRoot(d),
                            StringComparison.OrdinalIgnoreCase)))
                    .Select(ToImagingBitLockerVolume)
                    .OrderBy(static v => v.MountPoint, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                string model = Convert.ToString(disk["Model"])?.Trim() ?? string.Empty;
                string serialNumber = Convert.ToString(disk["SerialNumber"])?.Trim() ?? string.Empty;
                ulong sizeBytes = ReadUInt64(disk["Size"]);

                disks.Add(new ImagingDiskInfo
                {
                    DiskNumber = diskNumber,
                    DevicePath = Convert.ToString(disk["DeviceID"])?.Trim() ?? $@"\\.\PhysicalDrive{diskNumber}",
                    Model = model,
                    InterfaceType = Convert.ToString(disk["InterfaceType"])?.Trim() ?? string.Empty,
                    MediaType = Convert.ToString(disk["MediaType"])?.Trim() ?? string.Empty,
                    SerialNumber = serialNumber,
                    SizeBytes = sizeBytes,
                    IsOffline = storageInfo?.IsOffline,
                    StorageInfo = storageInfo,
                    StorageInfoAvailable = storageInfo != null,
                    StorageInfoError = storageDiskError,
                    PartitionStorageInfoAvailable = string.IsNullOrWhiteSpace(storagePartitionError),
                    PartitionStorageInfoError = storagePartitionError,
                    Partitions = partitions.OrderBy(static p => p.PartitionNumber).ToArray(),
                    BitLockerVolumes = diskBitLockerVolumes,
                    BitLockerStatusAvailable = bitLockerStatus.Available,
                    BitLockerStatusError = bitLockerStatus.Error
                });
            }
        }

        return disks.OrderBy(static d => d.DiskNumber).ToArray();
    }

    public static ImagingDiskInfo? FindDiskForPath(IEnumerable<ImagingDiskInfo> disks, string? path)
    {
        string? root = ImagingPath.TryGetDriveRootForPath(path);
        if (root == null)
            return null;

        return disks.FirstOrDefault(d => d.ContainsDrive(root));
    }

    private static Dictionary<int, ImagingDiskStorageInfo> GetStorageDisksBestEffort(out string error)
    {
        Dictionary<int, ImagingDiskStorageInfo> result = new();
        error = string.Empty;

        try
        {
            using ManagementObjectSearcher searcher = new(
                @"root\Microsoft\Windows\Storage",
                "SELECT * FROM MSFT_Disk");
            using ManagementObjectCollection disks = searcher.Get();

            foreach (ManagementObject disk in disks.Cast<ManagementObject>())
            {
                using (disk)
                {
                    int number = ReadInt32(GetPropertyValueSafe(disk, "Number"));
                    result[number] = new ImagingDiskStorageInfo
                    {
                        Path = ReadString(GetPropertyValueSafe(disk, "Path")),
                        Location = ReadString(GetPropertyValueSafe(disk, "Location")),
                        FriendlyName = ReadString(GetPropertyValueSafe(disk, "FriendlyName")),
                        UniqueId = ReadString(GetPropertyValueSafe(disk, "UniqueId")),
                        UniqueIdFormat = FormatUniqueIdFormat(ReadNullableUInt16(GetPropertyValueSafe(disk, "UniqueIdFormat"))),
                        SerialNumber = ReadString(GetPropertyValueSafe(disk, "SerialNumber")),
                        FirmwareVersion = ReadString(GetPropertyValueSafe(disk, "FirmwareVersion")),
                        Manufacturer = ReadString(GetPropertyValueSafe(disk, "Manufacturer")),
                        Model = ReadString(GetPropertyValueSafe(disk, "Model")),
                        SizeBytes = ReadUInt64(GetPropertyValueSafe(disk, "Size")),
                        AllocatedSizeBytes = ReadUInt64(GetPropertyValueSafe(disk, "AllocatedSize")),
                        LogicalSectorSize = ReadUInt32(GetPropertyValueSafe(disk, "LogicalSectorSize")),
                        PhysicalSectorSize = ReadUInt32(GetPropertyValueSafe(disk, "PhysicalSectorSize")),
                        LargestFreeExtentBytes = ReadUInt64(GetPropertyValueSafe(disk, "LargestFreeExtent")),
                        NumberOfPartitions = ReadUInt32(GetPropertyValueSafe(disk, "NumberOfPartitions")),
                        ProvisioningType = FormatProvisioningType(ReadNullableUInt16(GetPropertyValueSafe(disk, "ProvisioningType"))),
                        OperationalStatus = FormatDiskOperationalStatus(ReadNullableUInt16(GetPropertyValueSafe(disk, "OperationalStatus"))),
                        HealthStatus = FormatHealthStatus(ReadNullableUInt16(GetPropertyValueSafe(disk, "HealthStatus"))),
                        BusType = FormatBusType(ReadNullableUInt16(GetPropertyValueSafe(disk, "BusType"))),
                        PartitionStyle = FormatPartitionStyle(ReadNullableUInt16(GetPropertyValueSafe(disk, "PartitionStyle"))),
                        Signature = ReadNullableUInt32(GetPropertyValueSafe(disk, "Signature")),
                        Guid = ReadString(GetPropertyValueSafe(disk, "Guid")),
                        IsOffline = ReadNullableBoolean(GetPropertyValueSafe(disk, "IsOffline")),
                        OfflineReason = FormatOfflineReason(ReadNullableUInt16(GetPropertyValueSafe(disk, "OfflineReason"))),
                        IsReadOnly = ReadNullableBoolean(GetPropertyValueSafe(disk, "IsReadOnly")),
                        IsSystem = ReadNullableBoolean(GetPropertyValueSafe(disk, "IsSystem")),
                        IsClustered = ReadNullableBoolean(GetPropertyValueSafe(disk, "IsClustered")),
                        IsBoot = ReadNullableBoolean(GetPropertyValueSafe(disk, "IsBoot")),
                        BootFromDisk = ReadNullableBoolean(GetPropertyValueSafe(disk, "BootFromDisk"))
                    };
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return result;
    }

    private static Dictionary<(int DiskNumber, ulong OffsetBytes), ImagingPartitionStorageInfo> GetStoragePartitionsBestEffort(
        out string error)
    {
        Dictionary<(int DiskNumber, ulong OffsetBytes), ImagingPartitionStorageInfo> result = new();
        error = string.Empty;

        try
        {
            using ManagementObjectSearcher searcher = new(
                @"root\Microsoft\Windows\Storage",
                "SELECT * FROM MSFT_Partition");
            using ManagementObjectCollection partitions = searcher.Get();

            foreach (ManagementObject partition in partitions.Cast<ManagementObject>())
            {
                using (partition)
                {
                    int diskNumber = ReadInt32(GetPropertyValueSafe(partition, "DiskNumber"));
                    ulong offset = ReadUInt64(GetPropertyValueSafe(partition, "Offset"));
                    int storagePartitionNumber = ReadInt32(GetPropertyValueSafe(partition, "PartitionNumber"));
                    string driveLetter = ReadDriveLetter(GetPropertyValueSafe(partition, "DriveLetter"));

                    result[(diskNumber, offset)] = new ImagingPartitionStorageInfo
                    {
                        DiskNumber = diskNumber,
                        PartitionNumber = storagePartitionNumber,
                        DriveLetter = driveLetter,
                        AccessPaths = ReadStringArray(GetPropertyValueSafe(partition, "AccessPaths")),
                        OperationalStatus = FormatPartitionOperationalStatus(ReadNullableUInt16(GetPropertyValueSafe(partition, "OperationalStatus"))),
                        TransitionState = FormatPartitionTransitionState(ReadNullableUInt16(GetPropertyValueSafe(partition, "TransitionState"))),
                        SizeBytes = ReadUInt64(GetPropertyValueSafe(partition, "Size")),
                        OffsetBytes = offset,
                        MbrType = FormatMbrType(ReadNullableUInt16(GetPropertyValueSafe(partition, "MbrType"))),
                        GptType = FormatGptType(ReadString(GetPropertyValueSafe(partition, "GptType"))),
                        Guid = ReadString(GetPropertyValueSafe(partition, "Guid")),
                        IsReadOnly = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsReadOnly")),
                        IsOffline = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsOffline")),
                        IsSystem = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsSystem")),
                        IsBoot = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsBoot")),
                        IsActive = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsActive")),
                        IsHidden = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsHidden")),
                        IsShadowCopy = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsShadowCopy")),
                        NoDefaultDriveLetter = ReadNullableBoolean(GetPropertyValueSafe(partition, "NoDefaultDriveLetter"))
                    };
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return result;
    }

    private static Dictionary<int, List<ImagingPartitionInfo>> GetPartitions(
        IReadOnlyDictionary<(int DiskNumber, ulong OffsetBytes), ImagingPartitionStorageInfo> storagePartitions)
    {
        Dictionary<int, List<ImagingPartitionInfo>> result = new();

        using ManagementObjectSearcher searcher = new(
            @"root\CIMV2",
            "SELECT DiskIndex, Index, DeviceID, Type, Size, StartingOffset, BootPartition, PrimaryPartition FROM Win32_DiskPartition");
        using ManagementObjectCollection partitions = searcher.Get();

        foreach (ManagementObject partition in partitions.Cast<ManagementObject>())
        {
            using (partition)
            {
                int diskIndex = ReadInt32(partition["DiskIndex"]);
                string deviceId = Convert.ToString(partition["DeviceID"])?.Trim() ?? string.Empty;
                ulong offset = ReadUInt64(partition["StartingOffset"]);
                storagePartitions.TryGetValue((diskIndex, offset), out ImagingPartitionStorageInfo? storageInfo);
                int win32PartitionIndex = ReadInt32(partition["Index"]);
                int partitionNumber = storageInfo is { PartitionNumber: > 0 }
                    ? storageInfo.PartitionNumber
                    : win32PartitionIndex + 1;

                ImagingPartitionInfo info = new()
                {
                    PartitionNumber = partitionNumber,
                    Win32PartitionIndex = win32PartitionIndex,
                    DeviceId = deviceId,
                    Type = Convert.ToString(partition["Type"])?.Trim() ?? string.Empty,
                    SizeBytes = ReadUInt64(partition["Size"]),
                    StartingOffsetBytes = offset,
                    BootPartition = ReadBoolean(partition["BootPartition"]),
                    PrimaryPartition = ReadBoolean(partition["PrimaryPartition"]),
                    DriveLetters = GetLogicalDrivesForPartition(deviceId),
                    StorageInfo = storageInfo
                };

                if (!result.TryGetValue(diskIndex, out List<ImagingPartitionInfo>? list))
                {
                    list = new List<ImagingPartitionInfo>();
                    result.Add(diskIndex, list);
                }

                list.Add(info);
            }
        }

        return result;
    }

    private static IReadOnlyList<string> GetLogicalDrivesForPartition(string partitionDeviceId)
    {
        if (string.IsNullOrWhiteSpace(partitionDeviceId))
            return Array.Empty<string>();

        string escaped = partitionDeviceId.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        string query = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID=\"{escaped}\"}} WHERE AssocClass = Win32_LogicalDiskToPartition";
        List<string> drives = new();

        using ManagementObjectSearcher searcher = new(@"root\CIMV2", query);
        using ManagementObjectCollection logicalDisks = searcher.Get();

        foreach (ManagementObject logicalDisk in logicalDisks.Cast<ManagementObject>())
        {
            using (logicalDisk)
            {
                string drive = Convert.ToString(logicalDisk["DeviceID"])?.Trim() ?? string.Empty;
                string normalized = ImagingPath.NormalizeDriveRoot(drive);
                if (normalized.Length > 0)
                    drives.Add(normalized);
            }
        }

        return drives.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static d => d).ToArray();
    }

    private static BitLockerStatusSnapshot GetBitLockerVolumesBestEffort()
    {
        try
        {
            return new BitLockerStatusSnapshot(
                new BitLockerCompositeBackend().GetVolumes(),
                Available: true,
                Error: string.Empty);
        }
        catch (Exception ex)
        {
            return new BitLockerStatusSnapshot(
                Array.Empty<BitLockerVolumeInfo>(),
                Available: false,
                Error: ex.Message);
        }
    }

    private static ImagingBitLockerVolumeInfo ToImagingBitLockerVolume(BitLockerVolumeInfo volume)
    {
        string conversionStatus = GetStatusField(volume.StatusText, "Conversion Status");
        string protectionStatus = GetStatusField(volume.StatusText, "Protection Status");
        string percentageText = GetStatusField(volume.StatusText, "Percentage Encrypted");
        string encryptionType = GetStatusField(volume.StatusText, "Encryption Type");

        return new ImagingBitLockerVolumeInfo
        {
            MountPoint = volume.MountPoint,
            VolumeLabel = volume.VolumeLabel,
            IsLocked = volume.IsLocked,
            IsEncrypted = volume.IsEncrypted,
            IsBitLockerCapable = volume.IsBitLockerCapable,
            IsSystemVolume = volume.IsSystemVolume,
            VolumeTypeText = volume.VolumeTypeText,
            VisualState = volume.VisualState,
            EncryptionPercentage = ParsePercentage(percentageText),
            ConversionStatus = conversionStatus,
            EncryptionType = encryptionType,
            ProtectionStatus = protectionStatus
        };
    }

    private static string GetStatusField(string statusText, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(statusText))
            return string.Empty;

        string prefix = fieldName + ":";
        using StringReader reader = new(statusText);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return trimmed[prefix.Length..].Trim();
        }

        return string.Empty;
    }

    private static int? ParsePercentage(string value)
    {
        Match match = PercentageRegex.Match(value ?? string.Empty);
        if (!match.Success ||
            !double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return null;
        }

        return Math.Clamp((int)Math.Round(parsed), 0, 100);
    }

    private static object? GetPropertyValueSafe(ManagementObject obj, string propertyName)
    {
        try { return obj.Properties[propertyName]?.Value; }
        catch { return null; }
    }

    private static string ReadString(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

    private static IReadOnlyList<string> ReadStringArray(object? value)
    {
        if (value is string[] strings)
            return strings.Where(static s => !string.IsNullOrWhiteSpace(s)).Select(static s => s.Trim()).ToArray();
        if (value is IEnumerable<string> sequence)
            return sequence.Where(static s => !string.IsNullOrWhiteSpace(s)).Select(static s => s.Trim()).ToArray();
        return Array.Empty<string>();
    }

    private static string ReadDriveLetter(object? value)
    {
        if (value is char character && character != '\0')
            return char.ToUpperInvariant(character) + ":";

        try
        {
            if (value is ushort or short or uint or int)
            {
                char numericCharacter = Convert.ToChar(value, CultureInfo.InvariantCulture);
                if (numericCharacter != '\0' && char.IsLetter(numericCharacter))
                    return char.ToUpperInvariant(numericCharacter) + ":";
            }
        }
        catch
        {
        }

        string text = ReadString(value).TrimEnd(':');
        return text.Length == 1 && char.IsLetter(text[0]) ? char.ToUpperInvariant(text[0]) + ":" : string.Empty;
    }

    private static int ReadInt32(object? value)
    {
        try { return value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch { return 0; }
    }

    private static uint ReadUInt32(object? value)
    {
        try { return value == null ? 0U : Convert.ToUInt32(value, CultureInfo.InvariantCulture); }
        catch { return 0U; }
    }

    private static uint? ReadNullableUInt32(object? value)
    {
        try { return value == null ? null : Convert.ToUInt32(value, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static ushort? ReadNullableUInt16(object? value)
    {
        try { return value == null ? null : Convert.ToUInt16(value, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static ulong ReadUInt64(object? value)
    {
        try { return value == null ? 0UL : Convert.ToUInt64(value, CultureInfo.InvariantCulture); }
        catch { return 0UL; }
    }

    private static bool ReadBoolean(object? value)
    {
        try { return value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
        catch { return false; }
    }

    private static bool? ReadNullableBoolean(object? value)
    {
        try { return value == null ? null : Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static string FormatUniqueIdFormat(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "Vendor specific",
        1 => "Vendor ID",
        2 => "EUI-64",
        3 => "FCPH name",
        8 => "SCSI name string",
        _ => $"Unknown ({value})"
    };

    private static string FormatProvisioningType(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "Unknown",
        1 => "Thin",
        2 => "Fixed",
        _ => $"Unknown ({value})"
    };

    private static string FormatHealthStatus(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        5 => "Unknown",
        _ => $"Unknown ({value})"
    };

    private static string FormatDiskOperationalStatus(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "Unknown",
        1 => "Other",
        2 => "OK",
        3 => "Degraded",
        4 => "Stressed",
        5 => "Predictive failure",
        6 => "Error",
        7 => "Non-recoverable error",
        8 => "Starting",
        9 => "Stopping",
        10 => "Stopped",
        11 => "In service",
        12 => "No contact",
        13 => "Lost communication",
        14 => "Aborted",
        15 => "Dormant",
        16 => "Supporting entity in error",
        17 => "Completed",
        18 => "Power mode",
        0xD010 => "Online",
        0xD011 => "Not ready",
        0xD012 => "No media",
        0xD013 => "Offline",
        0xD014 => "Failed",
        _ => $"Unknown (0x{value:X4})"
    };

    private static string FormatBusType(ushort? value) => value switch
    {
        null => string.Empty,
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

    private static string FormatPartitionStyle(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "RAW",
        1 => "MBR",
        2 => "GPT",
        _ => $"Unknown ({value})"
    };

    private static string FormatOfflineReason(ushort? value) => value switch
    {
        null => string.Empty,
        0 => string.Empty,
        1 => "Policy",
        2 => "Redundant path",
        3 => "Snapshot",
        4 => "Signature/identifier collision",
        5 => "Resource exhaustion",
        6 => "Critical write failures",
        7 => "Data integrity scan required",
        _ => $"Unknown ({value})"
    };

    private static string FormatPartitionOperationalStatus(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "Unknown",
        1 => "Online",
        3 => "No media",
        4 => "Offline",
        5 => "Failed",
        _ => $"Unknown ({value})"
    };

    private static string FormatPartitionTransitionState(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "Unknown / reserved",
        1 => "Stable",
        2 => "Extending",
        3 => "Shrinking",
        4 => "Reconfiguring",
        8 => "Restriping",
        _ => $"Unknown ({value})"
    };

    private static string FormatMbrType(ushort? value) => value switch
    {
        null => string.Empty,
        0 => string.Empty,
        1 => "FAT12 (0x01)",
        4 => "FAT16 (0x04)",
        5 => "Extended (0x05)",
        6 => "Huge (0x06)",
        7 => "IFS / NTFS / exFAT (0x07)",
        12 => "FAT32 (0x0C)",
        _ => $"0x{value:X2}"
    };

    private static string FormatGptType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value.Trim().Trim('{', '}').ToLowerInvariant();
        string name = normalized switch
        {
            "c12a7328-f81f-11d2-ba4b-00a0c93ec93b" => "EFI System",
            "e3c9e316-0b5c-4db8-817d-f92df00215ae" => "Microsoft Reserved",
            "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7" => "Basic data",
            "5808c8aa-7e8f-42e0-85d2-e1e90434cfb3" => "LDM metadata",
            "af9b60a0-1431-4f62-bc68-3311714a69ad" => "LDM data",
            "de94bba4-06d1-4d40-a16a-bfd50179d6ac" => "Microsoft Recovery",
            _ => string.Empty
        };

        return string.IsNullOrEmpty(name) ? value.Trim() : $"{name} ({value.Trim()})";
    }

    private readonly record struct BitLockerStatusSnapshot(
        IReadOnlyList<BitLockerVolumeInfo> Volumes,
        bool Available,
        string Error);
}
