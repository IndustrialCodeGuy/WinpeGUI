using Imaging.Core;

namespace Imaging.Manager;

internal sealed class DeployWimConfirmDialog : ImagingConfirmationDialogBase
{
    private readonly WimImageSelector _imageSelector;
    private readonly CheckBox _confirm;
    private readonly Button _deploy;

    public DeployWimConfirmDialog(
        ImagingDiskInfo disk,
        string imagePath,
        IReadOnlyList<WimImageInfo> images,
        WimDeploymentFirmwareType firmwareType)
        : base("Deploy WIM", 620)
    {
        string firmwareText = firmwareType == WimDeploymentFirmwareType.Uefi
            ? "UEFI / GPT"
            : "BIOS / MBR";
        string layoutText = firmwareType == WimDeploymentFirmwareType.Uefi
            ? "260 MB EFI · 16 MB MSR · Windows · 900 MB Recovery"
            : "100 MB System · Windows · 750 MB Recovery";

        AddHeader($"Deploy WIM to Disk {disk.DiskNumber}?");

        string serial = string.IsNullOrWhiteSpace(disk.SerialNumber)
            ? string.Empty
            : $"\nSerial: {disk.SerialNumber}";
        AddTextBlock(
            $"Target: Disk {disk.DiskNumber}\n{disk.Model}\n" +
            $"Size: {FormatBytes(disk.SizeBytes)}{serial}\nFirmware/layout: {firmwareText}");
        AddSingleLine($"Partitions: {layoutText}");
        AddSingleLine($"WIM File: {imagePath}", gapAfter: 8);

        _imageSelector = new WimImageSelector(images, Font);
        AddControlRow(_imageSelector, _imageSelector.Height, gapAfter: 8);

        AddTextBlock(
            "This is a full deployment. DiskPart will clean the selected physical disk, recreate the " +
            "system/Windows/Recovery layout, apply the WIM, configure boot files, and configure Windows RE " +
            "when winre.wim is available.",
            gapAfter: 8,
            emphasis: true);

        _confirm = AddCheckBox(
            $"I understand all data on Disk {disk.DiskNumber} will be erased.",
            gapAfter: 0);
        Button cancel = CreateButton("Cancel", DialogResult.Cancel);
        _deploy = CreateButton("Deploy", DialogResult.OK, enabled: false);
        FinishLayout(new[] { cancel, _deploy }, gapBefore: 12);

        _confirm.CheckedChanged += (_, _) => UpdateDeployButton();
        _imageSelector.SelectionChanged += (_, _) => UpdateDeployButton();
        CancelButton = cancel;
        UpdateDeployButton();
    }

    public WimImageInfo SelectedImage => _imageSelector.SelectedImage;

    private void UpdateDeployButton() =>
        _deploy.Enabled = _confirm.Checked;
}
