using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class ApplyWimConfirmDialog : Form
{
    private readonly ComboBox _images;
    private readonly Label _description;
    private readonly CheckBox _confirm;
    private readonly Button _apply;

    public ApplyWimConfirmDialog(
        ImagingDiskInfo disk,
        ImagingPartitionInfo partition,
        string targetRoot,
        string imagePath,
        IReadOnlyList<WimImageInfo> images)
    {
        Text = "Apply WIM";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 392);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        string targetAccess = targetRoot.TrimEnd('\\');
        string targetName = partition.DriveLetters.Count > 0 && targetAccess.Length > 0
            ? targetAccess
            : $"Partition {partition.PartitionNumber}";

        Label header = new()
        {
            Left = 16,
            Top = 14,
            Width = 568,
            Height = 36,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"Apply WIM to {targetName}?"
        };

        string type = string.IsNullOrWhiteSpace(partition.Type) ? string.Empty : $"\nType: {partition.Type}";
        string temporaryAccess = partition.DriveLetters.Count == 0 && targetAccess.Length > 0
            ? $"\nTemporary access: {targetAccess}"
            : string.Empty;
        Label target = new()
        {
            Left = 16,
            Top = 52,
            Width = 568,
            Height = 72,
            AutoSize = false,
            Text = $"Target: Disk {disk.DiskNumber}, Partition {partition.PartitionNumber}\n" +
                   $"Size: {FormatBytes(partition.SizeBytes)}{type}{temporaryAccess}"
        };

        Label imagePathLabel = new()
        {
            Left = 16,
            Top = 126,
            Width = 568,
            Height = 38,
            AutoEllipsis = true,
            Text = $"WIM: {imagePath}"
        };

        Label imageLabel = new()
        {
            Left = 16,
            Top = 170,
            Width = 82,
            Height = 24,
            Text = "Image:"
        };

        _images = new ComboBox
        {
            Left = 100,
            Top = 168,
            Width = 484,
            Height = 26,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(WimImageInfo.DisplayName)
        };
        foreach (WimImageInfo image in images)
            _images.Items.Add(image);
        if (_images.Items.Count > 0)
            _images.SelectedIndex = 0;
        _images.SelectedIndexChanged += (_, _) => UpdateDescription();

        _description = new Label
        {
            Left = 100,
            Top = 198,
            Width = 484,
            Height = 44,
            AutoEllipsis = true
        };

        Label warning = new()
        {
            Left = 16,
            Top = 248,
            Width = 568,
            Height = 52,
            AutoSize = false,
            Font = new Font(Font, FontStyle.Bold),
            Text = "This applies files to the selected partition only. The partition is not formatted; existing files that are not replaced by the WIM may remain."
        };

        _confirm = new CheckBox
        {
            Left = 16,
            Top = 306,
            Width = 410,
            Height = 28,
            Text = "I understand files on the selected partition may be overwritten."
        };

        Button cancel = new()
        {
            Left = 410,
            Top = 344,
            Width = 80,
            Height = 32,
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        _apply = new Button
        {
            Left = 498,
            Top = 344,
            Width = 86,
            Height = 32,
            Text = "Apply Image",
            DialogResult = DialogResult.OK,
            Enabled = false
        };

        _confirm.CheckedChanged += (_, _) => UpdateApplyButton();
        _images.SelectedIndexChanged += (_, _) => UpdateApplyButton();

        Controls.AddRange(new Control[]
        {
            header, target, imagePathLabel, imageLabel, _images, _description,
            warning, _confirm, cancel, _apply
        });
        CancelButton = cancel;
        UpdateDescription();
        UpdateApplyButton();
    }

    public WimImageInfo SelectedImage => _images.SelectedItem as WimImageInfo
        ?? throw new InvalidOperationException("No WIM image is selected.");

    private void UpdateDescription()
    {
        if (_images.SelectedItem is not WimImageInfo image || string.IsNullOrWhiteSpace(image.Description))
        {
            _description.Text = string.Empty;
            return;
        }

        _description.Text = image.Description;
    }

    private void UpdateApplyButton() =>
        _apply.Enabled = _confirm.Checked && _images.SelectedItem is WimImageInfo;

    private static string FormatBytes(ulong bytes)
    {
        const double mb = 1024d * 1024d;
        const double gb = mb * 1024d;
        const double tb = gb * 1024d;
        if (bytes >= tb) return $"{bytes / tb:0.##} TB";
        if (bytes >= gb) return $"{bytes / gb:0.##} GB";
        if (bytes >= mb) return $"{bytes / mb:0.##} MB";
        return $"{bytes} B";
    }
}
