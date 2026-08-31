using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class ApplyWimConfirmDialog : Form
{
    private readonly ComboBox _images;
    private readonly Label _description;
    private readonly CheckBox _configureBoot;
    private readonly CheckBox _confirm;
    private readonly Button _apply;

    public ApplyWimConfirmDialog(
        ImagingDiskInfo disk,
        ImagingPartitionInfo partition,
        string targetRoot,
        string fileSystem,
        string imagePath,
        IReadOnlyList<WimImageInfo> images,
        bool configureBootByDefault)
    {
        Text = "Apply WIM";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 444);
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
            Height = 88,
            AutoSize = false,
            Text = $"Target: Disk {disk.DiskNumber}, Partition {partition.PartitionNumber}\n" +
                   $"Size: {FormatBytes(partition.SizeBytes)}\n" +
                   $"Filesystem: {fileSystem}{type}{temporaryAccess}"
        };

        Label imagePathLabel = new()
        {
            Left = 16,
            Top = 142,
            Width = 568,
            Height = 38,
            AutoEllipsis = true,
            Text = $"WIM: {imagePath}"
        };

        Label imageLabel = new()
        {
            Left = 16,
            Top = 186,
            Width = 82,
            Height = 24,
            Text = "Image:"
        };

        _images = new ComboBox
        {
            Left = 100,
            Top = 184,
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
            Top = 214,
            Width = 484,
            Height = 44,
            AutoEllipsis = true
        };

        Label warning = new()
        {
            Left = 16,
            Top = 264,
            Width = 568,
            Height = 52,
            AutoSize = false,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"The selected partition will be QUICK-FORMATTED as {fileSystem} before the WIM is applied. All existing files on this partition will be removed. Other partitions are not changed."
        };

        _configureBoot = new CheckBox
        {
            Left = 16,
            Top = 320,
            Width = 568,
            Height = 26,
            Text = "Configure Windows boot files after apply (BCDBoot).",
            Checked = configureBootByDefault
        };

        _confirm = new CheckBox
        {
            Left = 16,
            Top = 350,
            Width = 410,
            Height = 28,
            Text = "I understand all files on the selected partition will be erased."
        };

        Button cancel = new()
        {
            Left = 410,
            Top = 396,
            Width = 80,
            Height = 32,
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        _apply = new Button
        {
            Left = 498,
            Top = 396,
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
            warning, _configureBoot, _confirm, cancel, _apply
        });
        CancelButton = cancel;
        UpdateDescription();
        UpdateApplyButton();
    }

    public WimImageInfo SelectedImage => _images.SelectedItem as WimImageInfo
        ?? throw new InvalidOperationException("No WIM image is selected.");

    public bool ConfigureBootFiles => _configureBoot.Checked;

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
