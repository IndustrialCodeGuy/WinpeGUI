using Imaging.Core;
using System.Text;

namespace Imaging.Manager;

internal sealed class EncryptedCaptureWarningDialog : ImagingConfirmationDialogBase
{
    public EncryptedCaptureWarningDialog(ImagingDiskInfo disk, FfuCaptureAssessment assessment)
        : base("BitLocker Encryption Detected", 560)
    {
        AddHeader($"Disk {disk.DiskNumber} contains BitLocker-encrypted data.");
        AddTextBlock(BuildText(assessment), gapAfter: 0);

        Button manage = CreateButton("Review BitLocker", DialogResult.Retry, width: 140);
        Button cancel = CreateButton("Cancel", DialogResult.Cancel);
        Button anyway = CreateButton("Capture Anyway", DialogResult.Ignore, width: 110);
        FinishLayout(new[] { cancel, anyway }, leftButton: manage, gapBefore: 12);
        CancelButton = cancel;
    }

    private static string BuildText(FfuCaptureAssessment assessment)
    {
        StringBuilder text = new();
        text.AppendLine("Microsoft does not support FFU capture of encrypted disks. Unlocking or suspending BitLocker does not decrypt the sectors on disk, and encrypted data compresses poorly.");
        text.AppendLine();

        foreach (ImagingBitLockerVolumeInfo volume in assessment.AffectedVolumes)
        {
            string percent = volume.EncryptionPercentage.HasValue
                ? $"{volume.EncryptionPercentage.Value}% encrypted"
                : "encryption percentage unknown";
            string lockText = volume.IsLocked switch
            {
                true => "locked",
                false => "unlocked",
                _ => "lock state unknown"
            };
            string conversion = string.IsNullOrWhiteSpace(volume.ConversionStatus)
                ? "BitLocker active"
                : volume.ConversionStatus;
            string encryptionType = string.IsNullOrWhiteSpace(volume.EncryptionType)
                ? string.Empty
                : $", {volume.EncryptionType}";
            text.AppendLine(
                $"{volume.MountPoint.TrimEnd('\\')}: {conversion}, {percent}{encryptionType}, {lockText}");
        }

        text.AppendLine();
        text.Append("Fully decrypt the affected volume(s) before capture for the supported and normally much smaller FFU result.");
        return text.ToString();
    }
}
