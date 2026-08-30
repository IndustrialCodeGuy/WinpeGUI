using Imaging.Core;
using Shared.Shell.Theming;
using System.Text;

namespace Imaging.Manager;

internal sealed class EncryptedCaptureWarningDialog : Form
{
    public EncryptedCaptureWarningDialog(ImagingDiskInfo disk, FfuCaptureAssessment assessment)
    {
        Text = "BitLocker Encryption Detected";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 292);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        Label header = new()
        {
            Left = 16,
            Top = 14,
            Width = 528,
            Height = 44,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"Disk {disk.DiskNumber} contains BitLocker-encrypted data."
        };

        Label body = new()
        {
            Left = 16,
            Top = 58,
            Width = 528,
            Height = 154,
            AutoSize = false,
            Text = BuildText(assessment)
        };

        Button manage = new() { Left = 16, Top = 238, Width = 170, Height = 32, Text = "Review BitLocker", DialogResult = DialogResult.Retry };
        Button cancel = new() { Left = 342, Top = 238, Width = 84, Height = 32, Text = "Cancel", DialogResult = DialogResult.Cancel };
        Button anyway = new() { Left = 434, Top = 238, Width = 110, Height = 32, Text = "Capture Anyway", DialogResult = DialogResult.Ignore };

        Controls.AddRange(new Control[] { header, body, manage, cancel, anyway });
        CancelButton = cancel;
    }

    private static string BuildText(FfuCaptureAssessment assessment)
    {
        StringBuilder text = new();
        text.AppendLine("Microsoft does not support FFU capture of encrypted disks. Unlocking or suspending BitLocker does not decrypt the sectors on disk, and encrypted data compresses poorly.");
        text.AppendLine();

        foreach (ImagingBitLockerVolumeInfo volume in assessment.AffectedVolumes)
        {
            string percent = volume.EncryptionPercentage.HasValue ? $"{volume.EncryptionPercentage.Value}% encrypted" : "encryption percentage unknown";
            string lockText = volume.IsLocked switch { true => "locked", false => "unlocked", _ => "lock state unknown" };
            string conversion = string.IsNullOrWhiteSpace(volume.ConversionStatus) ? "BitLocker active" : volume.ConversionStatus;
            string encryptionType = string.IsNullOrWhiteSpace(volume.EncryptionType) ? string.Empty : $", {volume.EncryptionType}";
            text.AppendLine($"{volume.MountPoint.TrimEnd('\\')}: {conversion}, {percent}{encryptionType}, {lockText}");
        }

        text.AppendLine();
        text.Append("Fully decrypt the affected volume(s) before capture for the supported and normally much smaller FFU result.");
        return text.ToString();
    }
}
