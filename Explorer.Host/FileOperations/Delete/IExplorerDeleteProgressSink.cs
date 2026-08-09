namespace Explorer.Host.FileOperations.Delete;

internal enum ExplorerDeleteErrorAction
{
    Retry,
    Skip,
    Cancel
}

internal interface IExplorerDeleteProgressSink
{
    bool IsCancelled { get; }

    void InitializeProgress(long totalBytes, long totalItemCount);
    void ReportProgress(string operation, string sourcePath);
    void AdjustCompletedBytes(long bytesDelta);
    void AdjustCompletedItems(long itemsDelta);
    void CompleteProgress();

    ExplorerDeleteErrorAction HandleError(string sourcePath, Exception exception);
}