using Imaging.Core;

namespace Imaging.Manager;

internal sealed class WimExportProgressDialog : ImagingProgressDialogBase
{
    public WimExportProgressDialog(string sourcePath, string destinationPath, WimImageInfo image)
        : base(
            "Export WIM",
            $"Exporting {image.DisplayName}",
            "Preparing WIM export...",
            $"Source WIM: {sourcePath}",
            $"Destination WIM: {destinationPath}",
            "Cancel the WIM export?")
    {
    }

    public void UpdateProgress(WimOperationProgress progress) =>
        ApplyProgressUpdate(progress.Percentage, progress.Message);
}
