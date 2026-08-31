using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class DeployWimConfirmDialog : Form
{
    private readonly ComboBox? _images;
    private readonly WimImageInfo? _singleImage;
    private readonly Label _description;
    private readonly CheckBox _confirm;
    private readonly Button _deploy;

    public DeployWimConfirmDialog(
        ImagingDiskInfo disk,
        string imagePath,
        IReadOnlyList<WimImageInfo> images,
        WimDeploymentFirmwareType firmwareType)
    {
        if (images == null || images.Count == 0)
            throw new ArgumentException("At least one WIM image is required.", nameof(images));

        Text = "Deploy WIM";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 454);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        string firmwareText = firmwareType == WimDeploymentFirmwareType.Uefi
            ? "UEFI / GPT"
            : "BIOS / MBR";
        string layoutText = firmwareType == WimDeploymentFirmwareType.Uefi
            ? "260 MB EFI · 16 MB MSR · Windows · 900 MB Recovery"
            : "100 MB System · Windows · 750 MB Recovery";

        Label header = new()
        {
            Left = 16,
            Top = 14,
            Width = 588,
            Height = 36,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"Deploy WIM to Disk {disk.DiskNumber}?"
        };

        string serial = string.IsNullOrWhiteSpace(disk.SerialNumber) ? string.Empty : $"\nSerial: {disk.SerialNumber}";
        Label target = new()
        {
            Left = 16,
            Top = 52,
            Width = 588,
            Height = 94,
            AutoSize = false,
            Text = $"Target: Disk {disk.DiskNumber}\n{disk.Model}\nSize: {FormatBytes(disk.SizeBytes)}{serial}\nFirmware/layout: {firmwareText}"
        };

        Label layout = new()
        {
            Left = 16,
            Top = 148,
            Width = 588,
            Height = 28,
            AutoSize = false,
            Text = $"Partitions: {layoutText}"
        };

        Label imagePathLabel = new()
        {
            Left = 16,
            Top = 180,
            Width = 588,
            Height = 38,
            AutoEllipsis = true,
            Text = $"WIM: {imagePath}"
        };

        Label imageCaption = new()
        {
            Left = 16,
            Top = 224,
            Width = 82,
            Height = 24,
            Text = images.Count > 1 ? "Image:" : "Image:"
        };

        _description = new Label
        {
            Left = 100,
            Top = 254,
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
                Top = 222,
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
                Top = 220,
                Width = 504,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = nameof(WimImageInfo.DisplayName)
            };
            foreach (WimImageInfo image in images)
                _images.Items.Add(image);
            _images.SelectedIndex = 0;
            _images.SelectedIndexChanged += (_, _) =>
            {
                UpdateDescription();
                UpdateDeployButton();
            };
            Controls.Add(_images);
        }

        Label warning = new()
        {
            Left = 16,
            Top = 302,
            Width = 588,
            Height = 62,
            AutoSize = false,
            Font = new Font(Font, FontStyle.Bold),
            Text = "This is a full deployment. DiskPart will clean the selected physical disk, recreate the system/Windows/Recovery layout, apply the WIM, configure boot files, and configure Windows RE when winre.wim is available."
        };

        _confirm = new CheckBox
        {
            Left = 16,
            Top = 368,
            Width = 420,
            Height = 28,
            Text = $"I understand all data on Disk {disk.DiskNumber} will be erased."
        };

        Button cancel = new()
        {
            Left = 426,
            Top = 406,
            Width = 82,
            Height = 32,
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        _deploy = new Button
        {
            Left = 516,
            Top = 406,
            Width = 88,
            Height = 32,
            Text = "Deploy",
            DialogResult = DialogResult.OK,
            Enabled = false
        };

        _confirm.CheckedChanged += (_, _) => UpdateDeployButton();

        Controls.AddRange(new Control[]
        {
            header, target, layout, imagePathLabel, imageCaption, _description,
            warning, _confirm, cancel, _deploy
        });
        CancelButton = cancel;
        UpdateDescription();
        UpdateDeployButton();
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

    private void UpdateDeployButton()
    {
        bool hasImage = _singleImage != null || _images?.SelectedItem is WimImageInfo;
        _deploy.Enabled = _confirm.Checked && hasImage;
    }

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
