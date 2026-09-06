using Imaging.Core;

namespace Imaging.Manager;

internal sealed class DeleteWimImageConfirmDialog : ImagingConfirmationDialogBase
{
    private readonly WimImageSelector _imageSelector;

    public DeleteWimImageConfirmDialog(string imagePath, IReadOnlyList<WimImageInfo> images)
        : base("Delete WIM Image", 620)
    {
        if (images == null || images.Count <= 1)
            throw new ArgumentException("Delete WIM Image requires a WIM containing more than one image.", nameof(images));

        AddHeader("Delete an image from the WIM?");
        AddSingleLine($"WIM File: {imagePath}", gapAfter: 8);

        _imageSelector = new WimImageSelector(images, Font);
        AddControlRow(_imageSelector, _imageSelector.Height, gapAfter: 8);

        AddTextBlock(
            "DISM removes the selected image's metadata and XML entry. It does not reclaim unused WIM resource data. " +
            "Export the remaining images to a new WIM later if you need to compact the file.",
            gapAfter: 0);

        Button cancel = CreateButton("Cancel", DialogResult.Cancel);
        Button delete = CreateButton("Delete", DialogResult.OK);
        FinishLayout(new[] { cancel, delete }, gapBefore: 12);
        AcceptButton = delete;
        CancelButton = cancel;
    }

    public WimImageInfo SelectedImage => _imageSelector.SelectedImage;
}
