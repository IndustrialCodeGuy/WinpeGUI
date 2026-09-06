using Imaging.Core;

namespace Imaging.Manager;

internal sealed class WimCaptureProgressDialog : ImagingProgressDialogBase
{
    public WimCaptureProgressDialog(ImagingPartitionInfo partition, string sourceRoot, string imagePath)
        : base(
            "Capture WIM",
            $"Capturing {GetSourceName(partition, sourceRoot)}",
            "Preparing WIM capture...",
            $"WIM File: {imagePath}",
            secondaryDetail: null,
            cancelConfirmation: "Cancel the WIM capture?")
    {
    }

    public void UpdateProgress(WimOperationProgress progress) =>
        ApplyProgressUpdate(progress.Percentage, progress.Message);

    private static string GetSourceName(ImagingPartitionInfo partition, string sourceRoot)
    {
        string sourceName = sourceRoot.TrimEnd('\\');
        return sourceName.Length == 0
            ? $"Partition {partition.PartitionNumber}"
            : sourceName;
    }
}
