using Imaging.Core;
using Shared.Shell.Theming;
using System.Diagnostics;
using System.Globalization;

namespace Imaging.Manager;

public partial class MainForm
{
    private async Task CaptureSelectedDiskAsync()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        if (disk == null || _operationActive)
            return;

        FfuCaptureAssessment assessment = FfuCaptureAssessment.Evaluate(disk);
        if (assessment.Suitability == FfuCaptureSuitability.BitLockerStatusUnknown)
        {
            string detail = string.IsNullOrWhiteSpace(disk.BitLockerStatusError)
                ? string.Empty
                : $"\n\nStatus error:\n{disk.BitLockerStatusError}";

            DialogResult continueWithoutStatus = MessageBox.Show(
                this,
                "Imaging Manager could not verify the BitLocker encryption state of this disk." +
                "\n\nFFU capture of encrypted disks is unsupported. Verify that all source volumes are fully decrypted before capture." +
                detail +
                "\n\nContinue to the capture dialog anyway?",
                "BitLocker Status Unavailable",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (continueWithoutStatus != DialogResult.Yes)
                return;
        }

        if (assessment.RequiresEncryptionWarning)
        {
            using EncryptedCaptureWarningDialog warning = new(disk, assessment);
            DialogResult result = warning.ShowDialog(this);
            if (result == DialogResult.Retry)
            {
                LaunchBitLockerManager(assessment.AffectedVolumes.FirstOrDefault()?.MountPoint);
                return;
            }

            if (result != DialogResult.Ignore)
                return;
        }

        string? imagePath = RunExplorerPicker(save: true, title: $"Capture Disk {disk.DiskNumber} to FFU");
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!imagePath.EndsWith(".ffu", StringComparison.OrdinalIgnoreCase))
            imagePath += ".ffu";

        string? preflightError = ImagingPreflight.ValidateCaptureDestination(disk, _disks, imagePath);
        if (preflightError != null)
        {
            MessageBox.Show(this, preflightError, "Capture FFU", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (File.Exists(imagePath))
        {
            DialogResult replace = MessageBox.Show(
                this,
                $"The file already exists:\n\n{imagePath}\n\nReplace it?",
                "Capture FFU",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (replace != DialogResult.Yes)
                return;
        }

        string defaultName = Path.GetFileNameWithoutExtension(imagePath);
        using CaptureMetadataDialog metadata = new(defaultName);
        if (metadata.ShowDialog(this) != DialogResult.OK)
            return;

        await RunOperationAsync(
            FfuOperationKind.Capture,
            disk,
            imagePath,
            (operationImagePath, progress, token) => _ffuBackend.CaptureAsync(disk, operationImagePath, metadata.ImageName, metadata.Description, progress, token));
    }

    private async Task ApplyToSelectedDiskAsync()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        if (disk == null || _operationActive)
            return;

        string? imagePath = RunExplorerPicker(save: false, title: $"Select FFU to apply to Disk {disk.DiskNumber}");
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!File.Exists(imagePath))
        {
            MessageBox.Show(this, "The selected FFU file no longer exists.", "Apply FFU", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string? preflightError = ImagingPreflight.ValidateApplySourceAndRuntime(disk, _disks, imagePath, AppContext.BaseDirectory);
        if (preflightError != null)
        {
            MessageBox.Show(this, preflightError, "Apply FFU", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using ApplyFfuConfirmDialog confirm = new(disk, imagePath);
        if (confirm.ShowDialog(this) != DialogResult.OK)
            return;

        await RunOperationAsync(
            FfuOperationKind.Apply,
            disk,
            imagePath,
            (operationImagePath, progress, token) => _ffuBackend.ApplyAsync(disk, operationImagePath, progress, token));
    }
}
