using Imaging.Core;

namespace Imaging.Manager;

internal sealed class ExportWimConfirmDialog : ImagingConfirmationDialogBase
{
    private readonly WimImageSelector _imageSelector;

    public ExportWimConfirmDialog(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<WimImageInfo> images)
        : base("Export WIM", 620)
    {
        AddHeader("Export image to a new WIM?");
        AddSingleLine($"Source WIM: {sourcePath}");
        AddSingleLine($"Destination WIM: {destinationPath}", gapAfter: 8);

        _imageSelector = new WimImageSelector(images, Font);
        AddControlRow(_imageSelector, _imageSelector.Height, gapAfter: 8);

        AddTextBlock(
            "The selected image will be exported with maximum WIM compression and integrity checking. " +
            "Exporting to a new WIM also removes unneeded resource data left by prior WIM servicing.",
            gapAfter: 0);

        Button cancel = CreateButton("Cancel", DialogResult.Cancel);
        Button export = CreateButton("Export", DialogResult.OK);
        FinishLayout(new[] { cancel, export }, gapBefore: 12);
        AcceptButton = export;
        CancelButton = cancel;
    }

    public WimImageInfo SelectedImage => _imageSelector.SelectedImage;
}
