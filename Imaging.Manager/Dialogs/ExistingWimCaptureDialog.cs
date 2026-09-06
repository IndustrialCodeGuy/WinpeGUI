namespace Imaging.Manager;

internal sealed class ExistingWimCaptureDialog : ImagingConfirmationDialogBase
{
    public ExistingWimCaptureDialog(string imagePath)
        : base("Capture WIM", 500)
    {
        AddHeader("The selected WIM already exists:");
        AddSingleLine($"WIM File: {imagePath}", gapAfter: 8);
        AddTextBlock(
            "Append preserves the existing images and adds this capture as a new image index. " +
            "Append modifies the WIM in place; make sure the destination has sufficient free space.",
            gapAfter: 0);

        Button cancel = CreateButton("Cancel", DialogResult.Cancel, width: 86);
        Button replace = CreateButton("Replace", DialogResult.No, width: 92);
        Button append = CreateButton("Append", DialogResult.Yes, width: 96);
        FinishLayout(new[] { cancel, replace, append }, gapBefore: 12);
        AcceptButton = append;
        CancelButton = cancel;
    }
}
