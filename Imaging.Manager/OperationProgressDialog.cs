using Imaging.Core;

namespace Imaging.Manager;

internal sealed class OperationProgressDialog : ImagingProgressDialogBase
{
    public OperationProgressDialog(FfuOperationKind kind, ImagingDiskInfo disk, string imagePath)
        : base(
            kind == FfuOperationKind.Capture ? "Capture FFU" : "Apply FFU",
            kind == FfuOperationKind.Capture
                ? $"Capturing Disk {disk.DiskNumber}"
                : $"Applying FFU to Disk {disk.DiskNumber}",
            kind == FfuOperationKind.Capture
                ? "Preparing FFU capture..."
                : "Preparing FFU apply...",
            $"FFU File: {imagePath}",
            secondaryDetail: null,
            cancelConfirmation: kind == FfuOperationKind.Apply
                ? "Canceling an apply can leave the target disk incomplete. Cancel the operation?"
                : "Cancel the FFU capture?")
    {
    }

    public void UpdateProgress(FfuOperationProgress progress) =>
        ApplyProgressUpdate(progress.Percentage, progress.Message);
}
