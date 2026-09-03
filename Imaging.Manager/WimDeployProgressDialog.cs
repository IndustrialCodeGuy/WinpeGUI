using Imaging.Core;

namespace Imaging.Manager;

internal sealed class WimDeployProgressDialog : ImagingProgressDialogBase
{
    public WimDeployProgressDialog(ImagingDiskInfo disk, string imagePath, WimImageInfo image)
        : base(
            "Deploy WIM",
            $"Deploying {GetImageName(image)} to Disk {disk.DiskNumber}",
            "Preparing WIM deployment...",
            $"WIM File: {imagePath}",
            secondaryDetail: null,
            cancelConfirmation: "Cancel the WIM deployment?\n\nThe target disk may already have been erased and can be left unbootable or partially deployed.",
            cancelDialogTitle: "Cancel Deployment")
    {
    }

    public void UpdateProgress(WimDeploymentProgress progress) =>
        ApplyProgressUpdate(progress.Percentage, progress.Message, restoreMarquee: true);

    private static string GetImageName(WimImageInfo image) =>
        string.IsNullOrWhiteSpace(image.Name) ? $"Index {image.Index}" : image.Name;
}
