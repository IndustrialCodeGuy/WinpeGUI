using BitLocker.Core;

namespace Imaging.Core;

public sealed class ImagingDiskInfo
{
    public int DiskNumber { get; init; }
    public string DevicePath { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string InterfaceType { get; init; } = string.Empty;
    public string MediaType { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public ulong SizeBytes { get; init; }
    public bool? IsOffline { get; init; }
    public ImagingDiskStorageInfo? StorageInfo { get; init; }
    public bool StorageInfoAvailable { get; init; }
    public string StorageInfoError { get; init; } = string.Empty;
    public bool PartitionStorageInfoAvailable { get; init; }
    public string PartitionStorageInfoError { get; init; } = string.Empty;
    public IReadOnlyList<ImagingPartitionInfo> Partitions { get; init; } = Array.Empty<ImagingPartitionInfo>();
    public IReadOnlyList<ImagingBitLockerVolumeInfo> BitLockerVolumes { get; init; } = Array.Empty<ImagingBitLockerVolumeInfo>();
    public bool BitLockerStatusAvailable { get; init; }
    public string BitLockerStatusError { get; init; } = string.Empty;

    public string DisplayName => $"Disk {DiskNumber}";

    public string StableIdentity
    {
        get
        {
            string stableId = FirstNonEmpty(
                StorageInfo?.UniqueId,
                StorageInfo?.Guid,
                StorageInfo?.SerialNumber,
                SerialNumber);

            return string.IsNullOrWhiteSpace(stableId)
                ? $"number:{DiskNumber}"
                : $"stable:{stableId.Trim()}";
        }
    }

    public IEnumerable<string> DriveLetters => Partitions
        .SelectMany(static p => p.DriveLetters)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    public bool ContainsDrive(string? driveRoot)
    {
        string normalized = ImagingPath.NormalizeDriveRoot(driveRoot);
        return normalized.Length > 0 && DriveLetters.Any(d =>
            string.Equals(ImagingPath.NormalizeDriveRoot(d), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public sealed class ImagingInventorySnapshot
{
    public IReadOnlyList<ImagingDiskInfo> Disks { get; init; } = Array.Empty<ImagingDiskInfo>();
    public IReadOnlyList<ImagingVolumeInfo> OpticalVolumes { get; init; } = Array.Empty<ImagingVolumeInfo>();
}

public sealed class ImagingVolumeInfo
{
    public string MountPoint { get; init; } = string.Empty;
    public DriveType DriveType { get; init; }
    public bool IsReady { get; init; }
    public string VolumeLabel { get; init; } = string.Empty;
    public string DriveFormat { get; init; } = string.Empty;
    public ulong TotalSizeBytes { get; init; }
    public ulong TotalFreeSpaceBytes { get; init; }
    public ulong AvailableFreeSpaceBytes { get; init; }
    public bool ContainsOfflineWindowsInstall { get; init; }
    public bool IsRunningSystemDrive { get; init; }

    public ulong UsedSpaceBytes =>
        TotalSizeBytes >= TotalFreeSpaceBytes ? TotalSizeBytes - TotalFreeSpaceBytes : 0;

    public string DisplayName
    {
        get
        {
            string root = MountPoint.TrimEnd('\\');
            return string.IsNullOrWhiteSpace(VolumeLabel)
                ? root
                : $"{root} ({VolumeLabel})";
        }
    }
}

public sealed class ImagingDiskStorageInfo
{
    public string Path { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public string UniqueId { get; init; } = string.Empty;
    public string UniqueIdFormat { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string FirmwareVersion { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public ulong SizeBytes { get; init; }
    public ulong AllocatedSizeBytes { get; init; }
    public uint LogicalSectorSize { get; init; }
    public uint PhysicalSectorSize { get; init; }
    public ulong LargestFreeExtentBytes { get; init; }
    public uint NumberOfPartitions { get; init; }
    public string ProvisioningType { get; init; } = string.Empty;
    public string OperationalStatus { get; init; } = string.Empty;
    public string HealthStatus { get; init; } = string.Empty;
    public string BusType { get; init; } = string.Empty;
    public string PartitionStyle { get; init; } = string.Empty;
    public uint? Signature { get; init; }
    public string Guid { get; init; } = string.Empty;
    public bool? IsOffline { get; init; }
    public string OfflineReason { get; init; } = string.Empty;
    public bool? IsReadOnly { get; init; }
    public bool? IsSystem { get; init; }
    public bool? IsClustered { get; init; }
    public bool? IsBoot { get; init; }
    public bool? BootFromDisk { get; init; }
}

public sealed class ImagingPartitionInfo
{
    // PartitionNumber is the Storage/DiskPart number (normally one-based).
    // Win32PartitionIndex retains Win32_DiskPartition.Index for diagnostics.
    public int PartitionNumber { get; init; }
    public int Win32PartitionIndex { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public ulong SizeBytes { get; init; }
    public ulong StartingOffsetBytes { get; init; }
    public bool BootPartition { get; init; }
    public bool PrimaryPartition { get; init; }
    public IReadOnlyList<string> DriveLetters { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ImagingVolumeInfo> Volumes { get; init; } = Array.Empty<ImagingVolumeInfo>();
    public ImagingPartitionStorageInfo? StorageInfo { get; init; }

    public string StableIdentity
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(StorageInfo?.Guid))
                return $"guid:{StorageInfo.Guid.Trim()}";

            ulong offset = StorageInfo?.OffsetBytes ?? StartingOffsetBytes;
            return $"offset:{offset}";
        }
    }
}

public sealed class ImagingPartitionStorageInfo
{
    public int DiskNumber { get; init; }
    public int PartitionNumber { get; init; }
    public string DriveLetter { get; init; } = string.Empty;
    public IReadOnlyList<string> AccessPaths { get; init; } = Array.Empty<string>();
    public string OperationalStatus { get; init; } = string.Empty;
    public string TransitionState { get; init; } = string.Empty;
    public ulong SizeBytes { get; init; }
    public ulong OffsetBytes { get; init; }
    public string MbrType { get; init; } = string.Empty;
    public string GptType { get; init; } = string.Empty;
    public string Guid { get; init; } = string.Empty;
    public bool? IsReadOnly { get; init; }
    public bool? IsOffline { get; init; }
    public bool? IsSystem { get; init; }
    public bool? IsBoot { get; init; }
    public bool? IsActive { get; init; }
    public bool? IsHidden { get; init; }
    public bool? IsShadowCopy { get; init; }
    public bool? NoDefaultDriveLetter { get; init; }
}

public sealed class ImagingBitLockerVolumeInfo
{
    public string MountPoint { get; init; } = string.Empty;
    public string VolumeLabel { get; init; } = string.Empty;
    public bool? IsLocked { get; init; }
    public bool IsEncrypted { get; init; }
    public bool IsBitLockerCapable { get; init; }
    public bool IsSystemVolume { get; init; }
    public string VolumeTypeText { get; init; } = string.Empty;
    public BitLockerVisualState VisualState { get; init; }
    public int? EncryptionPercentage { get; init; }
    public string ConversionStatus { get; init; } = string.Empty;
    public string EncryptionType { get; init; } = string.Empty;
    public string ProtectionStatus { get; init; } = string.Empty;

    public bool HasEncryptionRemaining =>
        IsEncrypted ||
        EncryptionPercentage.GetValueOrDefault() > 0 ||
        (!string.IsNullOrWhiteSpace(ConversionStatus) &&
         !ConversionStatus.Equals("Fully Decrypted", StringComparison.OrdinalIgnoreCase));
}
