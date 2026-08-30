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
    public IReadOnlyList<ImagingPartitionInfo> Partitions { get; init; } = Array.Empty<ImagingPartitionInfo>();
    public IReadOnlyList<ImagingBitLockerVolumeInfo> BitLockerVolumes { get; init; } = Array.Empty<ImagingBitLockerVolumeInfo>();
    public bool BitLockerStatusAvailable { get; init; }
    public string BitLockerStatusError { get; init; } = string.Empty;

    public string DisplayName => $"Disk {DiskNumber}";

    public IEnumerable<string> DriveLetters => Partitions
        .SelectMany(static p => p.DriveLetters)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    public bool ContainsDrive(string? driveRoot)
    {
        string normalized = ImagingPath.NormalizeDriveRoot(driveRoot);
        return normalized.Length > 0 && DriveLetters.Any(d =>
            string.Equals(ImagingPath.NormalizeDriveRoot(d), normalized, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ImagingPartitionInfo
{
    public int PartitionNumber { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public ulong SizeBytes { get; init; }
    public bool BootPartition { get; init; }
    public bool PrimaryPartition { get; init; }
    public IReadOnlyList<string> DriveLetters { get; init; } = Array.Empty<string>();
}

public sealed class ImagingBitLockerVolumeInfo
{
    public string MountPoint { get; init; } = string.Empty;
    public string VolumeLabel { get; init; } = string.Empty;
    public bool? IsLocked { get; init; }
    public bool IsEncrypted { get; init; }
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
