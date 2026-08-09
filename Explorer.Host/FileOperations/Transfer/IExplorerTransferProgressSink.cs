namespace Explorer.Host.FileOperations.Transfer;

internal interface IExplorerTransferProgressSink
{
    bool IsCancelled { get; }

    void InitializeProgress(ExplorerTransferSummary summary);
    void ReportProgress(string operation, string sourcePath, string destinationPath);
    void AdjustCompletedBytes(long bytesDelta);
    void AdjustCompletedItems(long itemsDelta);
    void CompleteProgress();

    ExplorerTransferConflictDecision ResolveConflict(string sourcePath, string destinationPath);
    ExplorerTransferErrorAction HandleError(string sourcePath, string destinationPath, Exception exception, bool allowSkip);
}