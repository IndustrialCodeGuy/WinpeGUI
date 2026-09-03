using Imaging.Core;

namespace Imaging.Manager;

internal sealed class ApplyFfuConfirmDialog : ImagingConfirmationDialogBase
{
    private readonly Button _apply;

    public ApplyFfuConfirmDialog(ImagingDiskInfo disk, string imagePath)
        : base("Apply FFU", 570)
    {
        AddHeader($"Apply FFU to Disk {disk.DiskNumber}?");

        string serial = string.IsNullOrWhiteSpace(disk.SerialNumber)
            ? string.Empty
            : $"\nSerial: {disk.SerialNumber}";
        AddTextBlock(
            $"Target: Disk {disk.DiskNumber}\n{disk.Model}\nSize: {FormatBytes(disk.SizeBytes)}{serial}");
        AddSingleLine($"FFU File: {imagePath}", gapAfter: 8);
        AddTextBlock(
            "All partitions and data on the target physical disk will be overwritten.",
            gapAfter: 8,
            emphasis: true);

        CheckBox confirm = AddCheckBox(
            $"I understand Disk {disk.DiskNumber} will be overwritten.",
            gapAfter: 0);
        Button cancel = CreateButton("Cancel", DialogResult.Cancel);
        _apply = CreateButton("Apply Image", DialogResult.OK, width: 86, enabled: false);
        confirm.CheckedChanged += (_, _) => _apply.Enabled = confirm.Checked;

        FinishLayout(new[] { cancel, _apply }, gapBefore: 12);
        CancelButton = cancel;
    }
}
