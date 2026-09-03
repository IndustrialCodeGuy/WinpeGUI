using Imaging.Core;

namespace Imaging.Manager;

internal sealed class MountWimConfirmDialog : ImagingConfirmationDialogBase
{
    private readonly WimImageSelector _imageSelector;

    public MountWimConfirmDialog(
        string imagePath,
        string mountDirectory,
        IReadOnlyList<WimImageInfo> images)
        : base("Mount WIM", 620)
    {
        AddHeader("Mount WIM image?");
        AddSingleLine($"WIM File: {imagePath}");
        AddSingleLine($"Mount Dir: {mountDirectory}", gapAfter: 8);

        _imageSelector = new WimImageSelector(images, Font);
        AddControlRow(_imageSelector, _imageSelector.Height, gapAfter: 8);

        AddTextBlock(
            "The selected image will be mounted read/write. The mount folder must remain available until " +
            "the image is later unmounted with either Commit or Discard.",
            gapAfter: 0);

        Button cancel = CreateButton("Cancel", DialogResult.Cancel);
        Button mount = CreateButton("Mount", DialogResult.OK);
        FinishLayout(new[] { cancel, mount }, gapBefore: 12);
        AcceptButton = mount;
        CancelButton = cancel;
    }

    public WimImageInfo SelectedImage => _imageSelector.SelectedImage;
}
