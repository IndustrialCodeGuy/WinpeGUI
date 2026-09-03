using Imaging.Core;

namespace Imaging.Manager;

internal sealed class ApplyWimConfirmDialog : ImagingConfirmationDialogBase
{
    private readonly WimImageSelector _imageSelector;
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
        : base("Apply WIM", 600)
    {
        string targetAccess = targetRoot.TrimEnd('\\');
        string targetName = partition.DriveLetters.Count > 0 && targetAccess.Length > 0
            ? targetAccess
            : $"Partition {partition.PartitionNumber}";

        AddHeader($"Apply WIM to {targetName}?");

        string type = string.IsNullOrWhiteSpace(partition.Type)
            ? string.Empty
            : $"\nType: {partition.Type}";
        string temporaryAccess = partition.DriveLetters.Count == 0 && targetAccess.Length > 0
            ? $"\nTemporary access: {targetAccess}"
            : string.Empty;
        AddTextBlock(
            $"Target: Disk {disk.DiskNumber}, Partition {partition.PartitionNumber}\n" +
            $"Size: {FormatBytes(partition.SizeBytes)}\n" +
            $"Filesystem: {fileSystem}{type}{temporaryAccess}");
        AddSingleLine($"WIM File: {imagePath}", gapAfter: 8);

        _imageSelector = new WimImageSelector(images, Font);
        AddControlRow(_imageSelector, _imageSelector.Height, gapAfter: 8);

        AddTextBlock(
            $"The selected partition will be QUICK-FORMATTED as {fileSystem} before the WIM is applied. " +
            "All existing files on this partition will be removed. Other partitions are not changed.",
            gapAfter: 8,
            emphasis: true);

        _configureBoot = AddCheckBox(
            "Configure Windows boot files after apply (BCDBoot).",
            configureBootByDefault);
        _confirm = AddCheckBox(
            "I understand all files on the selected partition will be erased.",
            gapAfter: 0);

        Button cancel = CreateButton("Cancel", DialogResult.Cancel);
        _apply = CreateButton("Apply Image", DialogResult.OK, width: 86, enabled: false);
        FinishLayout(new[] { cancel, _apply }, gapBefore: 12);

        _confirm.CheckedChanged += (_, _) => UpdateApplyButton();
        _imageSelector.SelectionChanged += (_, _) => UpdateApplyButton();
        CancelButton = cancel;
        UpdateApplyButton();
    }

    public WimImageInfo SelectedImage => _imageSelector.SelectedImage;

    public bool ConfigureBootFiles => _configureBoot.Checked;

    private void UpdateApplyButton() =>
        _apply.Enabled = _confirm.Checked;
}
