using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class UnmountWimDialog : Form
{
    private readonly ComboBox? _images;
    private readonly WimMountedImageInfo? _singleImage;
    private readonly Label _details;
    private readonly Label _note;
    private readonly Button _commit;

    public UnmountWimDialog(IReadOnlyList<WimMountedImageInfo> images)
    {
        if (images == null || images.Count == 0)
            throw new ArgumentException("At least one mounted WIM is required.", nameof(images));

        Text = "Unmount WIM";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(650, 326);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        Label header = new()
        {
            Left = 16,
            Top = 14,
            Width = 618,
            Height = 30,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Unmount mounted WIM image?"
        };

        Label imageCaption = new()
        {
            Left = 16,
            Top = 58,
            Width = 92,
            Height = 24,
            Text = "Mounted WIM:"
        };

        _details = new Label
        {
            Left = 16,
            Top = 98,
            Width = 618,
            Height = 82,
            AutoEllipsis = true
        };

        if (images.Count == 1)
        {
            _singleImage = images[0];
            Label selected = new()
            {
                Left = 112,
                Top = 54,
                Width = 522,
                Height = 30,
                AutoEllipsis = true,
                Text = _singleImage.DisplayName
            };
            Controls.Add(selected);
        }
        else
        {
            _images = new ComboBox
            {
                Left = 112,
                Top = 52,
                Width = 522,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = nameof(WimMountedImageInfo.DisplayName)
            };
            foreach (WimMountedImageInfo image in images)
                _images.Items.Add(image);
            _images.SelectedIndex = 0;
            _images.SelectedIndexChanged += (_, _) => UpdateDetails();
            Controls.Add(_images);
        }

        _note = new Label
        {
            Left = 16,
            Top = 190,
            Width = 618,
            Height = 52,
            AutoSize = false,
            Text = "Commit saves changes back to the WIM. Discard abandons changes made while the image was mounted."
        };

        Button cancel = new()
        {
            Left = 356,
            Top = 278,
            Width = 80,
            Height = 32,
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        Button discard = new()
        {
            Left = 444,
            Top = 278,
            Width = 92,
            Height = 32,
            Text = "Discard",
            DialogResult = DialogResult.No
        };
        _commit = new Button
        {
            Left = 544,
            Top = 278,
            Width = 90,
            Height = 32,
            Text = "Commit",
            DialogResult = DialogResult.Yes
        };

        Controls.AddRange(new Control[] { header, imageCaption, _details, _note, cancel, discard, _commit });
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
        _details.Text = $"Image: {image.ImageFile}\nMount folder: {image.MountDirectory}\nMode: {mode}    Status: {status}";
        _commit.Enabled = image.ReadWrite;
        _note.Text = image.ReadWrite
            ? "Commit saves changes back to the WIM. Discard abandons changes made while the image was mounted."
            : "This image is mounted read-only, so there are no writable changes to commit. Use Discard to unmount it.";
    }
}
