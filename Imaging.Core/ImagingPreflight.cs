namespace Imaging.Core;

public static class ImagingPreflight
{
    public static string? ValidateCaptureDestination(
        ImagingDiskInfo sourceDisk,
        IReadOnlyList<ImagingDiskInfo> allDisks,
        string destinationPath)
    {
        ImagingDiskInfo? destinationDisk = DiskInventory.FindDiskForPath(allDisks, destinationPath);
        if (destinationDisk?.DiskNumber == sourceDisk.DiskNumber)
        {
            return $"The FFU cannot be saved to Disk {sourceDisk.DiskNumber} while that same physical disk is being captured.";
        }

        return null;
    }

    public static string? ValidateWimCaptureDestination(
        ImagingPartitionInfo sourcePartition,
        string destinationPath)
    {
        string? destinationRoot = ImagingPath.TryGetDriveRootForPath(destinationPath);
        if (destinationRoot == null)
            return null;

        bool samePartition = sourcePartition.DriveLetters.Any(d =>
            string.Equals(
                ImagingPath.NormalizeDriveRoot(d),
                destinationRoot,
                StringComparison.OrdinalIgnoreCase));

        if (samePartition)
            return $"The WIM cannot be saved to {destinationRoot.TrimEnd('\\')} while that same partition is being captured.";

        return null;
    }


    public static string? ValidateWimApplySourceAndRuntime(
        ImagingPartitionInfo targetPartition,
        string imagePath,
        string applicationBaseDirectory)
    {
        string? imageRoot = ImagingPath.TryGetDriveRootForPath(imagePath);
        if (imageRoot != null && PartitionContainsDrive(targetPartition, imageRoot))
        {
            return $"The WIM file is stored on {imageRoot.TrimEnd('\\')}, which is the selected target partition. " +
                   "Applying an image from the partition being modified is not supported.";
        }

        string? applicationRoot = ImagingPath.TryGetDriveRootForPath(applicationBaseDirectory);
        if (applicationRoot != null && PartitionContainsDrive(targetPartition, applicationRoot))
        {
            return $"Imaging Manager is running from {applicationRoot.TrimEnd('\\')}, which is the selected target partition. " +
                   "The partition hosting the running imaging tools cannot be used as the WIM apply target.";
        }

        return null;
    }

    private static bool PartitionContainsDrive(ImagingPartitionInfo partition, string driveRoot)
    {
        string normalized = ImagingPath.NormalizeDriveRoot(driveRoot);
        return normalized.Length > 0 && partition.DriveLetters.Any(d =>
            string.Equals(
                ImagingPath.NormalizeDriveRoot(d),
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    public static string? ValidateApplySourceAndRuntime(
        ImagingDiskInfo targetDisk,
        IReadOnlyList<ImagingDiskInfo> allDisks,
        string imagePath,
        string applicationBaseDirectory)
    {
        ImagingDiskInfo? imageDisk = DiskInventory.FindDiskForPath(allDisks, imagePath);
        if (imageDisk?.DiskNumber == targetDisk.DiskNumber)
        {
            return $"The FFU file is stored on Disk {targetDisk.DiskNumber}. Applying the image would overwrite the source image during the operation.";
        }

        ImagingDiskInfo? applicationDisk = DiskInventory.FindDiskForPath(allDisks, applicationBaseDirectory);
        if (applicationDisk?.DiskNumber == targetDisk.DiskNumber)
        {
            return $"Imaging Manager is running from Disk {targetDisk.DiskNumber}. The disk hosting the running imaging tools cannot be overwritten.";
        }

        return null;
    }
}
