using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class MountWimConfirmDialog : Form
{
    private readonly ComboBox? _images;
    private readonly WimImageInfo? _singleImage;
    private readonly Label _description;

    public MountWimConfirmDialog(
        string imagePath,
        string mountDirectory,
        IReadOnlyList<WimImageInfo> images)
    {
        if (images == null || images.Count == 0)
            throw new ArgumentException("At least one WIM image is required.", nameof(images));

        Text = "Mount WIM";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 326);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        Label header = new()
        {
            Left = 16,
            Top = 14,
            Width = 588,
            Height = 30,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Mount WIM image?"
        };

        Label source = new()
        {
            Left = 16,
            Top = 50,
            Width = 588,
            Height = 38,
            AutoEllipsis = true,
            Text = $"WIM: {imagePath}"
        };

        Label destination = new()
        {
            Left = 16,
            Top = 90,
            Width = 588,
            Height = 38,
            AutoEllipsis = true,
            Text = $"Mount folder: {mountDirectory}"
        };

        Label imageCaption = new()
        {
            Left = 16,
            Top = 136,
            Width = 82,
            Height = 24,
            Text = "Image:"
        };

        _description = new Label
        {
            Left = 100,
            Top = 168,
            Width = 504,
            Height = 42,
            AutoEllipsis = true
        };

        if (images.Count == 1)
        {
            _singleImage = images[0];
            Label selected = new()
            {
                Left = 100,
                Top = 134,
                Width = 504,
                Height = 26,
                AutoEllipsis = true,
                Text = _singleImage.DisplayName
            };
            Controls.Add(selected);
        }
        else
        {
            _images = new ComboBox
            {
                Left = 100,
                Top = 132,
                Width = 504,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = nameof(WimImageInfo.DisplayName)
            };
            foreach (WimImageInfo image in images)
                _images.Items.Add(image);
            _images.SelectedIndex = 0;
            _images.SelectedIndexChanged += (_, _) => UpdateDescription();
            Controls.Add(_images);
        }

        Label note = new()
        {
            Left = 16,
            Top = 218,
            Width = 588,
            Height = 46,
            AutoSize = false,
            Text = "The selected image will be mounted read/write. The mount folder must remain available until the image is later unmounted with either Commit or Discard."
        };

        Button cancel = new()
        {
            Left = 424,
            Top = 278,
            Width = 80,
            Height = 32,
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        Button mount = new()
        {
            Left = 512,
            Top = 278,
            Width = 92,
            Height = 32,
            Text = "Mount",
            DialogResult = DialogResult.OK
        };

        Controls.AddRange(new Control[]
        {
            header, source, destination, imageCaption, _description, note, cancel, mount
        });
        AcceptButton = mount;
        CancelButton = cancel;
        UpdateDescription();
    }

    public WimImageInfo SelectedImage => _singleImage
        ?? _images?.SelectedItem as WimImageInfo
        ?? throw new InvalidOperationException("No WIM image is selected.");

    private void UpdateDescription()
    {
        WimImageInfo? image = _singleImage ?? _images?.SelectedItem as WimImageInfo;
        _description.Text = image == null || string.IsNullOrWhiteSpace(image.Description)
            ? string.Empty
            : image.Description;
    }
}
