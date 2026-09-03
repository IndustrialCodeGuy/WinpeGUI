using Imaging.Core;

namespace Imaging.Manager;

internal sealed class WimApplyProgressDialog : ImagingProgressDialogBase
{
    public WimApplyProgressDialog(
        ImagingPartitionInfo partition,
        string targetRoot,
        string imagePath,
        WimImageInfo image)
        : base(
            "Apply WIM",
            GetHeading(partition, targetRoot, image),
            "Preparing WIM apply...",
            $"WIM File: {imagePath}",
            secondaryDetail: null,
            cancelConfirmation: "Cancel the WIM apply operation?\n\nThe target partition may be left with a partially applied image.")
    {
    }

    public void UpdateProgress(WimOperationProgress progress) =>
        ApplyProgressUpdate(progress.Percentage, progress.Message);

    public void BeginBootConfiguration() =>
        SetIndeterminateStatus("Configuring boot files...", disableCancel: true);

    private static string GetHeading(
        ImagingPartitionInfo partition,
        string targetRoot,
        WimImageInfo image)
    {
        string targetAccess = targetRoot.TrimEnd('\\');
        string targetName = partition.DriveLetters.Count > 0 && targetAccess.Length > 0
            ? targetAccess
            : $"Partition {partition.PartitionNumber}";
        string imageName = string.IsNullOrWhiteSpace(image.Name)
            ? $"Index {image.Index}"
            : image.Name;

        return $"Applying {imageName} to {targetName}";
    }
}
