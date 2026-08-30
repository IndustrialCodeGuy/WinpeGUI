using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class ApplyFfuConfirmDialog : Form
{
    private readonly CheckBox _confirm;
    private readonly Button _apply;

    public ApplyFfuConfirmDialog(ImagingDiskInfo disk, string imagePath)
    {
        Text = "Apply FFU";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(570, 326);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        Label header = new()
        {
            Left = 16,
            Top = 14,
            Width = 538,
            Height = 42,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"Apply FFU to Disk {disk.DiskNumber}?"
        };

        string serial = string.IsNullOrWhiteSpace(disk.SerialNumber) ? "" : $"\nSerial: {disk.SerialNumber}";
        Label details = new()
        {
            Left = 16,
            Top = 58,
            Width = 538,
            Height = 144,
            AutoSize = false,
            Text = $"Target: Disk {disk.DiskNumber}\n{disk.Model}\n{FormatBytes(disk.SizeBytes)}{serial}\n\nImage: {imagePath}"
        };

        Label warning = new()
        {
            Left = 16,
            Top = 206,
            Width = 538,
            Height = 32,
            AutoSize = false,
            Font = new Font(Font, FontStyle.Bold),
            Text = "All partitions and data on the target physical disk will be overwritten."
        };

        _confirm = new CheckBox
        {
            Left = 16,
            Top = 242,
            Width = 330,
            Height = 28,
            Text = $"I understand Disk {disk.DiskNumber} will be overwritten."
        };

        Button cancel = new() { Left = 382, Top = 278, Width = 80, Height = 32, Text = "Cancel", DialogResult = DialogResult.Cancel };
        _apply = new Button { Left = 470, Top = 278, Width = 84, Height = 32, Text = "Apply Image", DialogResult = DialogResult.OK, Enabled = false };
        _confirm.CheckedChanged += (_, _) => _apply.Enabled = _confirm.Checked;

        Controls.AddRange(new Control[] { header, details, warning, _confirm, cancel, _apply });
        CancelButton = cancel;
    }

    private static string FormatBytes(ulong bytes)
    {
        const double gb = 1024d * 1024d * 1024d;
        const double tb = gb * 1024d;
        return bytes >= tb ? $"{bytes / tb:0.##} TB" : $"{bytes / gb:0.##} GB";
    }
}
