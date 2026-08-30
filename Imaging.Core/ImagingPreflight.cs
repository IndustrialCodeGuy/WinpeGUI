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
