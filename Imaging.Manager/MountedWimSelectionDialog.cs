using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class MountedWimSelectionDialog : Form
{
    private readonly ComboBox? _images;
    private readonly WimMountedImageInfo? _singleImage;
    private readonly Label _details;
    private readonly Button _select;

    public MountedWimSelectionDialog(string title, string headerText, IReadOnlyList<WimMountedImageInfo> images)
    {
        if (images == null || images.Count == 0)
            throw new ArgumentException("At least one mounted WIM is required.", nameof(images));

        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 250);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        Label header = new()
        {
            Left = 16,
            Top = 14,
            Width = 588,
            Height = 30,
            Font = new Font(Font, FontStyle.Bold),
            Text = headerText
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
            Width = 588,
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
                Width = 492,
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
                Width = 492,
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

        Button cancel = new()
        {
            Left = 424,
            Top = 202,
            Width = 80,
            Height = 32,
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        _select = new Button
        {
            Left = 512,
            Top = 202,
            Width = 92,
            Height = 32,
            Text = "Select",
            DialogResult = DialogResult.OK
        };

        Controls.AddRange(new Control[] { header, imageCaption, _details, cancel, _select });
        AcceptButton = _select;
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
            return;
        }

        string mode = image.ReadWrite ? "Read/write" : "Read-only";
        string status = string.IsNullOrWhiteSpace(image.Status) ? "Unknown" : image.Status;
        _details.Text = $"Image: {image.ImageFile}\nMount folder: {image.MountDirectory}\nMode: {mode}    Status: {status}";
    }
}
