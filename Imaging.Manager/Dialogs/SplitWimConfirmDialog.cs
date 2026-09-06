namespace Imaging.Manager;

internal sealed class SplitWimConfirmDialog : ImagingConfirmationDialogBase
{
    private readonly NumericUpDown _fileSize;

    public SplitWimConfirmDialog(string sourcePath, string destinationPath)
        : base("Split WIM", 620)
    {
        AddHeader("Split WIM into SWM files?");
        AddSingleLine($"Source WIM: {sourcePath}");
        AddSingleLine($"First SWM: {destinationPath}", gapAfter: 8);

        Panel sizeRow = new();
        Label sizeLabel = new()
        {
            AutoSize = false,
            Text = "Maximum file size (MB):",
            TextAlign = ContentAlignment.MiddleLeft
        };
        _fileSize = new NumericUpDown
        {
            Minimum = 1,
            Maximum = int.MaxValue,
            Value = 3800,
            ThousandsSeparator = true,
            TextAlign = HorizontalAlignment.Right
        };
        sizeRow.Controls.Add(sizeLabel);
        sizeRow.Controls.Add(_fileSize);
        sizeRow.Resize += (_, _) =>
        {
            int controlWidth = 120;
            sizeLabel.SetBounds(0, 0, Math.Max(1, sizeRow.ClientSize.Width - controlWidth - 8), sizeRow.ClientSize.Height);
            _fileSize.SetBounds(Math.Max(0, sizeRow.ClientSize.Width - controlWidth), 0, controlWidth, sizeRow.ClientSize.Height);
        };
        AddControlRow(sizeRow, 26, gapAfter: 8);

        AddTextBlock(
            "3800 MB is a safe default for FAT32 deployment media. DISM may create a larger SWM part if a single file in the image is larger than the requested limit.",
            gapAfter: 0);

        Button cancel = CreateButton("Cancel", DialogResult.Cancel);
        Button split = CreateButton("Split", DialogResult.OK);
        FinishLayout(new[] { cancel, split }, gapBefore: 12);
        AcceptButton = split;
        CancelButton = cancel;
    }

    public int FileSizeMb => decimal.ToInt32(_fileSize.Value);
}
