using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class UnmountWimDialog : ImagingConfirmationDialogBase
{
    private readonly ComboBox? _images;
    private readonly WimMountedImageInfo? _singleImage;
    private readonly Label _details;
    private readonly Label _note;
    private readonly Button _commit;

    public UnmountWimDialog(IReadOnlyList<WimMountedImageInfo> images)
        : base("Unmount WIM", 650)
    {
        if (images == null || images.Count == 0)
            throw new ArgumentException("At least one mounted WIM is required.", nameof(images));

        AddHeader("Unmount mounted WIM image?");

        Panel selector = new()
        {
            Font = Font,
            BackColor = ShellTheme.WindowBack,
            ForeColor = ShellTheme.TextColor
        };
        selector.Controls.Add(new Label
        {
            Left = 0,
            Top = 2,
            Width = 92,
            Height = BodyLineHeight,
            Text = "Mounted WIM:"
        });

        if (images.Count == 1)
        {
            _singleImage = images[0];
            selector.Controls.Add(new Label
            {
                Left = 100,
                Top = 2,
                Width = ContentWidth - 100,
                Height = BodyLineHeight,
                AutoEllipsis = true,
                Text = _singleImage.DisplayName
            });
        }
        else
        {
            _images = new ComboBox
            {
                Left = 100,
                Top = 0,
                Width = ContentWidth - 100,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = nameof(WimMountedImageInfo.DisplayName)
            };
            foreach (WimMountedImageInfo image in images)
                _images.Items.Add(image);
            _images.SelectedIndex = 0;
            selector.Controls.Add(_images);
        }
        AddControlRow(selector, 26, gapAfter: 8);

        _details = new Label { AutoEllipsis = true };
        AddControlRow(_details, BodyLineHeight * 3, gapAfter: 8);

        _note = AddTextBlock(
            "Commit saves changes to the WIM first, then releases the mount. Discard releases the mount " +
            "without saving pending changes.",
            gapAfter: 0);

        Button cancel = CreateButton("Cancel", DialogResult.Cancel);
        Button discard = CreateButton("Discard", DialogResult.No, width: 92);
        _commit = CreateButton("Commit", DialogResult.Yes, width: 90);
        FinishLayout(new[] { cancel, discard, _commit }, gapBefore: 12);

        if (_images != null)
            _images.SelectedIndexChanged += (_, _) => UpdateDetails();

        AcceptButton = _commit;
        CancelButton = cancel;
        UpdateDetails();
    }

    public WimMountedImageInfo SelectedImage => _singleImage
        ?? _images?.SelectedItem as WimMountedImageInfo
        ?? throw new InvalidOperationException("No mounted WIM is selected.");

    private void UpdateDetails()
    {
        WimMountedImageInfo? image = _singleImage ?? _images?.SelectedItem as WimMountedImageInfo;
        if (image == null)
        {
            _details.Text = string.Empty;
            _commit.Enabled = false;
            return;
        }

        string mode = image.ReadWrite ? "Read/write" : "Read-only";
        string status = string.IsNullOrWhiteSpace(image.Status) ? "Unknown" : image.Status;
        _details.Text =
            $"WIM File: {image.ImageFile}\n" +
            $"Mount Dir: {image.MountDirectory}\n" +
            $"Mode: {mode}    Status: {status}";
        _commit.Enabled = image.ReadWrite;
        _note.Text = image.ReadWrite
            ? "Commit saves changes to the WIM first, then releases the mount. Discard releases the mount without saving pending changes."
            : "This image is mounted read-only, so there are no writable changes to commit. Use Discard to unmount it.";
    }
}
