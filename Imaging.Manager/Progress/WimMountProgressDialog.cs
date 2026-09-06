using Imaging.Core;

namespace Imaging.Manager;

internal sealed class WimMountProgressDialog : ImagingProgressDialogBase
{
    public WimMountProgressDialog(string imagePath, string mountDirectory, WimImageInfo image)
        : base(
            "Mount WIM",
            $"Mounting {image.DisplayName}",
            "Preparing WIM mount...",
            $"WIM File: {imagePath}",
            $"Mount Dir: {mountDirectory}")
    {
    }

    public void UpdateProgress(WimOperationProgress progress) =>
        ApplyProgressUpdate(progress.Percentage, progress.Message);
}
