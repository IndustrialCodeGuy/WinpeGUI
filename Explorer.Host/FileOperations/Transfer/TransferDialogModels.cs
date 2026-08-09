namespace Explorer.Host.FileOperations.Transfer;

internal enum ExplorerTransferConflictAction
{
    Overwrite,
    CopyWithNewName,
    Skip,
    Cancel
}

internal enum ExplorerTransferErrorAction
{
    Retry,
    Skip,
    Cancel
}

internal sealed class ExplorerTransferConflictDecision
{
    public ExplorerTransferConflictAction Action { get; init; }
    public bool ApplyToAll { get; init; }
}

internal sealed class ExplorerTransferConflictItem
{
    public ExplorerTransferConflictItem(string sourcePath, string destinationPath)
    {
        SourcePath = sourcePath ?? string.Empty;
        DestinationPath = destinationPath ?? string.Empty;
    }

    public string SourcePath { get; }
    public string DestinationPath { get; }

    public string FileName
    {
        get
        {
            string fileName = Path.GetFileName(SourcePath);
            return string.IsNullOrWhiteSpace(fileName) ? SourcePath : fileName;
        }
    }
}

internal sealed class ExplorerTransferSummary
{
    public static ExplorerTransferSummary Empty { get; } = new();

    public long TotalBytes { get; init; }
    public long TotalItemCount { get; init; }
    public long ConflictFileCount { get; init; }
    public bool IsSingleTopLevelFile { get; init; }
    public string SourceFolderPath { get; init; } = string.Empty;
    public string DestinationFolderPath { get; init; } = string.Empty;
    public IReadOnlyList<ExplorerTransferConflictItem> ConflictItems { get; init; } = Array.Empty<ExplorerTransferConflictItem>();
}
