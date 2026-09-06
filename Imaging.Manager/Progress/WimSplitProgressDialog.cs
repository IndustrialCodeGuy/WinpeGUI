using Imaging.Core;

namespace Imaging.Manager;

internal sealed class WimSplitProgressDialog : ImagingProgressDialogBase
{
    public WimSplitProgressDialog(string sourcePath, string destinationPath)
        : base(
            "Split WIM",
            "Splitting WIM image",
            "Preparing WIM split...",
            $"Source WIM: {sourcePath}",
            $"First SWM: {destinationPath}",
            "Cancel the WIM split?")
    {
    }

    public void UpdateProgress(WimOperationProgress progress) =>
        ApplyProgressUpdate(progress.Percentage, progress.Message);
}
