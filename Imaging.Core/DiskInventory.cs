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
        Dictionary<int, List<ImagingPartitionInfo>> partitionsByDisk = GetPartitions();
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

                disks.Add(new ImagingDiskInfo
                {
                    DiskNumber = diskNumber,
                    DevicePath = Convert.ToString(disk["DeviceID"])?.Trim() ?? $@"\\.\PhysicalDrive{diskNumber}",
                    Model = Convert.ToString(disk["Model"])?.Trim() ?? string.Empty,
                    InterfaceType = Convert.ToString(disk["InterfaceType"])?.Trim() ?? string.Empty,
                    MediaType = Convert.ToString(disk["MediaType"])?.Trim() ?? string.Empty,
                    SerialNumber = Convert.ToString(disk["SerialNumber"])?.Trim() ?? string.Empty,
                    SizeBytes = ReadUInt64(disk["Size"]),
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

    private static Dictionary<int, List<ImagingPartitionInfo>> GetPartitions()
    {
        Dictionary<int, List<ImagingPartitionInfo>> result = new();

        using ManagementObjectSearcher searcher = new(
            @"root\CIMV2",
            "SELECT DiskIndex, Index, DeviceID, Type, Size, BootPartition, PrimaryPartition FROM Win32_DiskPartition");
        using ManagementObjectCollection partitions = searcher.Get();

        foreach (ManagementObject partition in partitions.Cast<ManagementObject>())
        {
            using (partition)
            {
                int diskIndex = ReadInt32(partition["DiskIndex"]);
                string deviceId = Convert.ToString(partition["DeviceID"])?.Trim() ?? string.Empty;

                ImagingPartitionInfo info = new()
                {
                    PartitionNumber = ReadInt32(partition["Index"]),
                    DeviceId = deviceId,
                    Type = Convert.ToString(partition["Type"])?.Trim() ?? string.Empty,
                    SizeBytes = ReadUInt64(partition["Size"]),
                    BootPartition = ReadBoolean(partition["BootPartition"]),
                    PrimaryPartition = ReadBoolean(partition["PrimaryPartition"]),
                    DriveLetters = GetLogicalDrivesForPartition(deviceId)
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

    private readonly record struct BitLockerStatusSnapshot(
        IReadOnlyList<BitLockerVolumeInfo> Volumes,
        bool Available,
        string Error);

    private static int ReadInt32(object? value)
    {
        try { return value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch { return 0; }
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
}
