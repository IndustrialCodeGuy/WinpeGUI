using Explorer.Host.FileOperations.Clipboard;
using Shell.Core.Interfaces;
using Shell.Core.Models;
using System.IO;

namespace Explorer.Host.FileOperations.Transfer;

internal static class ExplorerTransferEngine
{
    private enum OperationStepResult
    {
        Completed,
        Skipped,
        Deferred,
        Canceled
    }

    private sealed class DeferredSourceDirectoryCleanup
    {
        public DeferredSourceDirectoryCleanup(string sourceDirectory, string destinationDirectory, bool isTopLevelSourceDirectory)
        {
            SourceDirectory = sourceDirectory;
            DestinationDirectory = destinationDirectory;
            IsTopLevelSourceDirectory = isTopLevelSourceDirectory;
        }

        public string SourceDirectory { get; }
        public string DestinationDirectory { get; }
        public bool IsTopLevelSourceDirectory { get; }
    }

    private sealed class TransferFailure
    {
        public TransferFailure(
            string sourcePath,
            string destinationPath,
            Exception exception,
            Action retryAction,
            bool tryBeforePrompt,
            bool allowSkip,
            long progressBytes,
            long progressItemCount)
        {
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
            Exception = exception;
            RetryAction = retryAction;
            TryBeforePrompt = tryBeforePrompt;
            AllowSkip = allowSkip;
            ProgressBytes = Math.Max(0, progressBytes);
            ProgressItemCount = Math.Max(0, progressItemCount);
        }

        public string SourcePath { get; }
        public string DestinationPath { get; }
        public Exception Exception { get; set; }
        public Action RetryAction { get; set; }
        public bool TryBeforePrompt { get; }
        public bool AllowSkip { get; }
        public long ProgressBytes { get; set; }
        public long ProgressItemCount { get; set; }
    }

    public static bool ExecuteTransfer(
        string destinationFolder,
        ExplorerTransferManifest manifest,
        IExplorerTransferProgressSink progressSink,
        IFileSystemChangeNotifier changeNotifier,
        RefreshReason reason)
    {
        if (string.IsNullOrWhiteSpace(destinationFolder) || !Directory.Exists(destinationFolder))
            return false;

        if (manifest.SourcePaths.Count == 0)
            return false;

        TransferChangeSet changeSet = new();
        List<TransferFailure> deferredFailures = [];
        List<DeferredSourceDirectoryCleanup> deferredSourceDirectoryCleanup = [];

        try
        {
            ExplorerTransferSummary summary = CreateTransferSummary(destinationFolder, manifest);
            progressSink.InitializeProgress(summary);

            foreach (string sourcePath in manifest.SourcePaths)
            {
                if (progressSink.IsCancelled)
                    return false;

                if (string.IsNullOrWhiteSpace(sourcePath))
                    continue;

                if (Directory.Exists(sourcePath))
                {
                    OperationStepResult directoryResult = TransferDirectoryEntry(
                        sourcePath,
                        destinationFolder,
                        manifest.Move,
                        progressSink,
                        changeSet,
                        deferredFailures,
                        deferredSourceDirectoryCleanup,
                        topLevelSourceDirectory: sourcePath);

                    if (directoryResult == OperationStepResult.Canceled)
                        return false;

                    continue;
                }

                if (File.Exists(sourcePath))
                {
                    OperationStepResult fileResult = TransferFileEntry(sourcePath, destinationFolder, manifest.Move, progressSink, changeSet, deferredFailures);
                    if (fileResult == OperationStepResult.Canceled)
                        return false;
                }
            }

            if (deferredFailures.Count > 0)
                changeSet.Apply(changeNotifier, reason);

            OperationStepResult failureResult = ProcessDeferredFailures(deferredFailures, progressSink);
            if (failureResult == OperationStepResult.Canceled)
                return false;

            OperationStepResult cleanupResult = RunDeferredSourceDirectoryCleanup(
                deferredSourceDirectoryCleanup,
                progressSink,
                changeSet);

            if (cleanupResult == OperationStepResult.Canceled)
                return false;

            progressSink.CompleteProgress();
            return !progressSink.IsCancelled;
        }
        finally
        {
            changeSet.Apply(changeNotifier, reason);
        }
    }

    private static OperationStepResult TransferDirectoryEntry(
        string sourceDirectory,
        string destinationParentFolder,
        bool move,
        IExplorerTransferProgressSink progressSink,
        TransferChangeSet changeSet,
        List<TransferFailure> deferredFailures,
        List<DeferredSourceDirectoryCleanup> deferredSourceDirectoryCleanup,
        string topLevelSourceDirectory)
    {
        string folderName = Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName))
            return OperationStepResult.Completed;

        if (PathEqualsOrIsDescendantOf(destinationParentFolder, sourceDirectory))
        {
            (long directoryBytes, long directoryItemCount) = GetDirectoryMetricsSafe(sourceDirectory);
            DeferStaticFailure(
                sourceDirectory,
                destinationParentFolder,
                new IOException("A folder cannot be copied or moved into itself or one of its subfolders."),
                deferredFailures,
                allowSkip: true,
                progressBytes: directoryBytes,
                progressItemCount: directoryItemCount);

            return OperationStepResult.Deferred;
        }

        string destinationDirectory = GetDestinationDirectoryPath(sourceDirectory, destinationParentFolder, move);

        if (PathsEqual(sourceDirectory, destinationDirectory))
        {
            MarkDirectoryCompleted(sourceDirectory, progressSink);
            return OperationStepResult.Completed;
        }

        progressSink.ReportProgress(move ? "Moving folder..." : "Copying folder...", sourceDirectory, destinationDirectory);

        if (IsDirectoryReparsePoint(sourceDirectory))
        {
            return TransferDirectoryReparsePoint(
                sourceDirectory,
                destinationDirectory,
                move,
                progressSink,
                changeSet,
                deferredFailures);
        }

        if (File.Exists(destinationDirectory))
        {
            (long directoryBytes, long directoryItemCount) = GetDirectoryMetricsSafe(sourceDirectory);
            DeferStaticFailure(
                sourceDirectory,
                destinationDirectory,
                new IOException("A file with the same name already exists in the destination folder."),
                deferredFailures,
                allowSkip: true,
                progressBytes: directoryBytes,
                progressItemCount: directoryItemCount);

            return OperationStepResult.Deferred;
        }

        if (move &&
            !Directory.Exists(destinationDirectory) &&
            IsSameVolumePath(sourceDirectory, destinationDirectory))
        {
            (long directoryBytes, long directoryItemCount) = GetDirectoryMetricsSafe(sourceDirectory);
            Action moveDirectory = () => Directory.Move(sourceDirectory, destinationDirectory);
            Action retryMoveDirectory = () =>
            {
                progressSink.ReportProgress("Moving folder...", sourceDirectory, destinationDirectory);
                moveDirectory();
                MarkCompletedItems(directoryBytes, directoryItemCount, progressSink);
                changeSet.RecordFolderRelocated(sourceDirectory, destinationDirectory);
            };

            OperationStepResult moveResult = RunOrDefer(
                sourceDirectory,
                destinationDirectory,
                moveDirectory,
                retryMoveDirectory,
                deferredFailures,
                tryBeforePrompt: true,
                allowSkip: true,
                progressBytes: directoryBytes,
                progressItemCount: directoryItemCount);

            if (moveResult == OperationStepResult.Canceled)
                return OperationStepResult.Canceled;

            if (moveResult == OperationStepResult.Completed)
            {
                MarkCompletedItems(directoryBytes, directoryItemCount, progressSink);
                changeSet.RecordFolderRelocated(sourceDirectory, destinationDirectory);
            }

            return moveResult;
        }

        if (!Directory.Exists(destinationDirectory))
        {
            OperationStepResult createResult = RunWithRetrySkipCancel(
                sourceDirectory,
                destinationDirectory,
                () => Directory.CreateDirectory(destinationDirectory),
                progressSink,
                allowSkip: false);

            if (createResult != OperationStepResult.Completed)
                return OperationStepResult.Canceled;

            changeSet.RecordFolderChildrenChanged(destinationParentFolder);
        }

        OperationStepResult enumerateResult = TryGetDirectoryChildren(
            sourceDirectory,
            destinationDirectory,
            progressSink,
            out string[] childDirectories,
            out string[] childFiles);

        if (enumerateResult == OperationStepResult.Canceled)
            return OperationStepResult.Canceled;

        if (enumerateResult == OperationStepResult.Skipped)
        {
            MarkDirectoryCompleted(sourceDirectory, progressSink);
            return OperationStepResult.Skipped;
        }

        progressSink.AdjustCompletedItems(1);

        bool deferredSomething = false;

        foreach (string childDirectory in childDirectories)
        {
            if (progressSink.IsCancelled)
                return OperationStepResult.Canceled;

            OperationStepResult childResult = TransferDirectoryEntry(
                childDirectory,
                destinationDirectory,
                move,
                progressSink,
                changeSet,
                deferredFailures,
                deferredSourceDirectoryCleanup,
                topLevelSourceDirectory);

            if (childResult == OperationStepResult.Canceled)
                return OperationStepResult.Canceled;

            if (childResult == OperationStepResult.Deferred)
                deferredSomething = true;
        }

        foreach (string childFile in childFiles)
        {
            if (progressSink.IsCancelled)
                return OperationStepResult.Canceled;

            OperationStepResult childResult = TransferFileEntry(
                childFile,
                destinationDirectory,
                move,
                progressSink,
                changeSet,
                deferredFailures);

            if (childResult == OperationStepResult.Canceled)
                return OperationStepResult.Canceled;

            if (childResult == OperationStepResult.Deferred)
                deferredSomething = true;
        }

        if (!move)
            return deferredSomething ? OperationStepResult.Deferred : OperationStepResult.Completed;

        if (!Directory.Exists(sourceDirectory))
            return OperationStepResult.Completed;

        if (TryDirectoryHasChildren(sourceDirectory, out bool hasChildren) && hasChildren)
        {
            if (deferredSomething)
            {
                AddDeferredSourceDirectoryCleanup(
                    deferredSourceDirectoryCleanup,
                    sourceDirectory,
                    destinationDirectory,
                    PathsEqual(sourceDirectory, topLevelSourceDirectory));
            }

            return deferredSomething ? OperationStepResult.Deferred : OperationStepResult.Completed;
        }

        Action deleteSourceDirectory = () => Directory.Delete(sourceDirectory, recursive: false);
        Action retryDeleteSourceDirectory = () =>
        {
            progressSink.ReportProgress("Moving folder...", sourceDirectory, destinationDirectory);
            deleteSourceDirectory();

            if (PathsEqual(sourceDirectory, topLevelSourceDirectory))
                changeSet.RecordFolderRelocated(sourceDirectory, destinationDirectory);
        };

        OperationStepResult deleteResult = RunOrDefer(
            sourceDirectory,
            destinationDirectory,
            deleteSourceDirectory,
            retryDeleteSourceDirectory,
            deferredFailures,
            tryBeforePrompt: true,
            allowSkip: true,
            progressBytes: 0,
            progressItemCount: 0);

        if (deleteResult == OperationStepResult.Canceled)
            return OperationStepResult.Canceled;

        if (deleteResult == OperationStepResult.Completed &&
            PathsEqual(sourceDirectory, topLevelSourceDirectory))
        {
            changeSet.RecordFolderRelocated(sourceDirectory, destinationDirectory);
        }

        return deleteResult;
    }

    private static OperationStepResult TransferDirectoryReparsePoint(
        string sourceDirectory,
        string destinationDirectory,
        bool move,
        IExplorerTransferProgressSink progressSink,
        TransferChangeSet changeSet,
        List<TransferFailure> deferredFailures)
    {
        const long progressItemCount = 1;

        if (File.Exists(destinationDirectory))
        {
            DeferStaticFailure(
                sourceDirectory,
                destinationDirectory,
                new IOException("A file with the same name already exists in the destination folder."),
                deferredFailures,
                allowSkip: true,
                progressBytes: 0,
                progressItemCount: progressItemCount);

            return OperationStepResult.Deferred;
        }

        if (Directory.Exists(destinationDirectory))
        {
            DeferStaticFailure(
                sourceDirectory,
                destinationDirectory,
                new IOException("A folder with the same name already exists in the destination folder."),
                deferredFailures,
                allowSkip: true,
                progressBytes: 0,
                progressItemCount: progressItemCount);

            return OperationStepResult.Deferred;
        }

        if (!move)
        {
            DeferStaticFailure(
                sourceDirectory,
                destinationDirectory,
                new NotSupportedException("This folder is a symbolic link or junction. Copying directory links is not supported."),
                deferredFailures,
                allowSkip: true,
                progressBytes: 0,
                progressItemCount: progressItemCount);

            return OperationStepResult.Deferred;
        }

        if (!IsSameVolumePath(sourceDirectory, destinationDirectory))
        {
            DeferStaticFailure(
                sourceDirectory,
                destinationDirectory,
                new NotSupportedException("This folder is a symbolic link or junction. Moving directory links across volumes is not supported."),
                deferredFailures,
                allowSkip: true,
                progressBytes: 0,
                progressItemCount: progressItemCount);

            return OperationStepResult.Deferred;
        }

        Action moveDirectoryLink = () => Directory.Move(sourceDirectory, destinationDirectory);
        Action recordMoveSuccess = () =>
        {
            MarkCompletedItems(0, progressItemCount, progressSink);
            changeSet.RecordFolderRelocated(sourceDirectory, destinationDirectory);
        };

        OperationStepResult moveResult = RunOrDefer(
            sourceDirectory,
            destinationDirectory,
            moveDirectoryLink,
            () =>
            {
                progressSink.ReportProgress("Moving folder...", sourceDirectory, destinationDirectory);
                moveDirectoryLink();
                recordMoveSuccess();
            },
            deferredFailures,
            tryBeforePrompt: true,
            allowSkip: true,
            progressBytes: 0,
            progressItemCount: progressItemCount);

        if (moveResult == OperationStepResult.Completed)
            recordMoveSuccess();

        return moveResult;
    }

    private static OperationStepResult TransferFileEntry(
        string sourceFile,
        string destinationFolder,
        bool move,
        IExplorerTransferProgressSink progressSink,
        TransferChangeSet changeSet,
        List<TransferFailure> deferredFailures)
    {
        string fileName = Path.GetFileName(sourceFile);
        if (string.IsNullOrWhiteSpace(fileName))
            return OperationStepResult.Completed;

        string? sourceParentFolder = TryGetParentPath(sourceFile);
        string destinationPath = GetDestinationFilePath(sourceFile, destinationFolder, move);

        if (PathsEqual(sourceFile, destinationPath))
        {
            MarkFileCompleted(sourceFile, progressSink);
            return OperationStepResult.Completed;
        }

        if (Directory.Exists(destinationPath))
        {
            DeferStaticFailure(
                sourceFile,
                destinationPath,
                new IOException("A folder with the same name already exists in the destination folder."),
                deferredFailures,
                allowSkip: true,
                progressBytes: GetFileLengthSafe(sourceFile),
                progressItemCount: 1);

            return OperationStepResult.Deferred;
        }

        bool overwriteExistingDestination = false;

        if (File.Exists(destinationPath))
        {
            progressSink.ReportProgress(move ? "Moving file..." : "Copying file...", sourceFile, destinationPath);

            ExplorerTransferConflictDecision decision = progressSink.ResolveConflict(sourceFile, destinationPath);
            switch (decision.Action)
            {
                case ExplorerTransferConflictAction.Overwrite:
                    overwriteExistingDestination = true;
                    break;

                case ExplorerTransferConflictAction.Skip:
                    MarkFileCompleted(sourceFile, progressSink);
                    return OperationStepResult.Completed;

                case ExplorerTransferConflictAction.CopyWithNewName:
                    destinationPath = GetNumberedConflictCopyPath(destinationPath);
                    break;

                case ExplorerTransferConflictAction.Cancel:
                    return OperationStepResult.Canceled;
            }
        }

        progressSink.ReportProgress(move ? "Moving file..." : "Copying file...", sourceFile, destinationPath);

        if (move && IsSameVolumePath(sourceFile, destinationPath))
        {
            long fileLength = GetFileLengthSafe(sourceFile);
            Action moveFile = () => MoveFileDirect(sourceFile, destinationPath, overwriteExistingDestination);
            Action recordMoveSuccess = () =>
            {
                MarkCompletedItems(fileLength, 1, progressSink);
                changeSet.RecordFileChanged(destinationFolder);

                if (!string.IsNullOrWhiteSpace(sourceParentFolder) &&
                    !PathsEqual(sourceParentFolder, destinationFolder))
                {
                    changeSet.RecordFileChanged(sourceParentFolder);
                }
            };

            OperationStepResult moveResult = RunOrDefer(
                sourceFile,
                destinationPath,
                moveFile,
                () =>
                {
                    progressSink.ReportProgress("Moving file...", sourceFile, destinationPath);
                    moveFile();
                    recordMoveSuccess();
                },
                deferredFailures,
                tryBeforePrompt: true,
                allowSkip: true,
                progressBytes: fileLength,
                progressItemCount: 1);

            if (moveResult == OperationStepResult.Canceled)
                return OperationStepResult.Canceled;

            if (moveResult == OperationStepResult.Completed)
                recordMoveSuccess();

            return moveResult;
        }

        long copiedFileLength = GetFileLengthSafe(sourceFile);
        Action copyFile = () => CopyFileWithProgress(sourceFile, destinationPath, progressSink, overwriteExistingDestination);
        Action recordCopySuccess = () =>
        {
            progressSink.AdjustCompletedItems(1);
            changeSet.RecordFileChanged(destinationFolder);
        };
        Action deleteSourceFile = () => File.Delete(sourceFile);
        Action recordSourceDeleteSuccess = () =>
        {
            if (!string.IsNullOrWhiteSpace(sourceParentFolder) &&
                !PathsEqual(sourceParentFolder, destinationFolder))
            {
                changeSet.RecordFileChanged(sourceParentFolder);
            }
        };

        TransferFailure? deferredMoveCopyFailure = null;
        Action retryCopy = () =>
        {
            progressSink.ReportProgress(move ? "Moving file..." : "Copying file...", sourceFile, destinationPath);
            copyFile();
            recordCopySuccess();

            if (!move)
                return;

            if (deferredMoveCopyFailure != null)
            {
                deferredMoveCopyFailure.ProgressBytes = 0;
                deferredMoveCopyFailure.ProgressItemCount = 0;
                deferredMoveCopyFailure.RetryAction = () =>
                {
                    progressSink.ReportProgress("Moving file...", sourceFile, destinationPath);
                    deleteSourceFile();
                    recordSourceDeleteSuccess();
                };
            }

            deleteSourceFile();
            recordSourceDeleteSuccess();
        };

        OperationStepResult copyResult = RunOrDefer(
            sourceFile,
            destinationPath,
            copyFile,
            retryCopy,
            deferredFailures,
            tryBeforePrompt: true,
            allowSkip: true,
            progressBytes: copiedFileLength,
            progressItemCount: 1,
            configureFailure: failure => deferredMoveCopyFailure = failure);

        if (copyResult == OperationStepResult.Canceled)
            return OperationStepResult.Canceled;

        if (copyResult == OperationStepResult.Completed)
            recordCopySuccess();

        if (copyResult == OperationStepResult.Deferred || !move)
            return copyResult;

        OperationStepResult deleteResult = RunOrDefer(
            sourceFile,
            destinationPath,
            deleteSourceFile,
            () =>
            {
                progressSink.ReportProgress("Moving file...", sourceFile, destinationPath);
                deleteSourceFile();
                recordSourceDeleteSuccess();
            },
            deferredFailures,
            tryBeforePrompt: true,
            allowSkip: true,
            progressBytes: 0,
            progressItemCount: 0);

        if (deleteResult == OperationStepResult.Canceled)
            return OperationStepResult.Canceled;

        if (deleteResult == OperationStepResult.Completed)
            recordSourceDeleteSuccess();

        return deleteResult;
    }

    private static OperationStepResult RunOrDefer(
        string sourcePath,
        string destinationPath,
        Action action,
        Action retryAction,
        List<TransferFailure> deferredFailures,
        bool tryBeforePrompt,
        bool allowSkip,
        long progressBytes,
        long progressItemCount,
        Action<TransferFailure>? configureFailure = null)
    {
        try
        {
            action();
            return OperationStepResult.Completed;
        }
        catch (OperationCanceledException)
        {
            return OperationStepResult.Canceled;
        }
        catch (Exception ex)
        {
            TransferFailure failure = new(
                sourcePath,
                destinationPath,
                ex,
                retryAction,
                tryBeforePrompt,
                allowSkip,
                progressBytes,
                progressItemCount);

            configureFailure?.Invoke(failure);
            deferredFailures.Add(failure);

            return OperationStepResult.Deferred;
        }
    }

    private static void DeferStaticFailure(
        string sourcePath,
        string destinationPath,
        Exception exception,
        List<TransferFailure> deferredFailures,
        bool allowSkip,
        long progressBytes,
        long progressItemCount)
    {
        deferredFailures.Add(new TransferFailure(
            sourcePath,
            destinationPath,
            exception,
            () => { throw exception; },
            tryBeforePrompt: false,
            allowSkip,
            progressBytes,
            progressItemCount));
    }

    private static OperationStepResult ProcessDeferredFailures(
        List<TransferFailure> deferredFailures,
        IExplorerTransferProgressSink progressSink)
    {
        foreach (TransferFailure failure in deferredFailures)
        {
            if (progressSink.IsCancelled)
                return OperationStepResult.Canceled;

            Exception? silentRetryException = null;
            if (failure.TryBeforePrompt && TryRetryFailure(failure, out silentRetryException))
                continue;

            if (silentRetryException != null)
                failure.Exception = silentRetryException;

            while (true)
            {
                if (progressSink.IsCancelled)
                    return OperationStepResult.Canceled;

                ExplorerTransferErrorAction response = progressSink.HandleError(
                    failure.SourcePath,
                    failure.DestinationPath,
                    failure.Exception,
                    failure.AllowSkip);

                if (response == ExplorerTransferErrorAction.Skip && failure.AllowSkip)
                {
                    progressSink.AdjustCompletedBytes(failure.ProgressBytes);
                    progressSink.AdjustCompletedItems(failure.ProgressItemCount);
                    break;
                }

                if (response == ExplorerTransferErrorAction.Cancel || response == ExplorerTransferErrorAction.Skip)
                    return OperationStepResult.Canceled;

                if (TryRetryFailure(failure, out Exception? retryException))
                    break;

                failure.Exception = retryException ?? failure.Exception;
            }
        }

        return OperationStepResult.Completed;
    }

    private static bool TryRetryFailure(TransferFailure failure, out Exception? exception)
    {
        try
        {
            failure.RetryAction();
            exception = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            exception = null;
            return false;
        }
        catch (Exception ex)
        {
            exception = ex;
            return false;
        }
    }

    private static OperationStepResult RunDeferredSourceDirectoryCleanup(
        List<DeferredSourceDirectoryCleanup> deferredSourceDirectoryCleanup,
        IExplorerTransferProgressSink progressSink,
        TransferChangeSet changeSet)
    {
        IEnumerable<DeferredSourceDirectoryCleanup> cleanupItems = deferredSourceDirectoryCleanup
            .Where(static item => !string.IsNullOrWhiteSpace(item.SourceDirectory))
            .GroupBy(static item => item.SourceDirectory, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderByDescending(static item => item.SourceDirectory.Length);

        foreach (DeferredSourceDirectoryCleanup cleanup in cleanupItems)
        {
            if (progressSink.IsCancelled)
                return OperationStepResult.Canceled;

            if (!Directory.Exists(cleanup.SourceDirectory))
                continue;

            progressSink.ReportProgress("Moving folder...", cleanup.SourceDirectory, cleanup.DestinationDirectory);

            try
            {
                Directory.Delete(cleanup.SourceDirectory, recursive: false);

                if (cleanup.IsTopLevelSourceDirectory)
                    changeSet.RecordFolderRelocated(cleanup.SourceDirectory, cleanup.DestinationDirectory);
            }
            catch
            {
                // The source folder can remain when a failed or skipped child still exists.
            }
        }

        return OperationStepResult.Completed;
    }

    private static void AddDeferredSourceDirectoryCleanup(
        List<DeferredSourceDirectoryCleanup> deferredSourceDirectoryCleanup,
        string sourceDirectory,
        string destinationDirectory,
        bool isTopLevelSourceDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
            return;

        deferredSourceDirectoryCleanup.Add(new DeferredSourceDirectoryCleanup(
            sourceDirectory,
            destinationDirectory,
            isTopLevelSourceDirectory));
    }

    private static OperationStepResult RunWithRetrySkipCancel(
        string sourcePath,
        string destinationPath,
        Action action,
        IExplorerTransferProgressSink progressSink,
        bool allowSkip)
    {
        while (true)
        {
            if (progressSink.IsCancelled)
                return OperationStepResult.Canceled;

            try
            {
                action();
                return OperationStepResult.Completed;
            }
            catch (OperationCanceledException)
            {
                return OperationStepResult.Canceled;
            }
            catch (Exception ex)
            {
                ExplorerTransferErrorAction response = progressSink.HandleError(sourcePath, destinationPath, ex, allowSkip);

                if (response == ExplorerTransferErrorAction.Retry)
                    continue;

                if (response == ExplorerTransferErrorAction.Skip && allowSkip)
                    return OperationStepResult.Skipped;

                return OperationStepResult.Canceled;
            }
        }
    }

    private static OperationStepResult TryGetDirectoryChildren(
        string sourceDirectory,
        string destinationDirectory,
        IExplorerTransferProgressSink progressSink,
        out string[] childDirectories,
        out string[] childFiles)
    {
        childDirectories = [];
        childFiles = [];

        while (true)
        {
            if (progressSink.IsCancelled)
                return OperationStepResult.Canceled;

            try
            {
                childDirectories = Directory.GetDirectories(sourceDirectory);
                childFiles = Directory.GetFiles(sourceDirectory);
                return OperationStepResult.Completed;
            }
            catch (Exception ex)
            {
                ExplorerTransferErrorAction response = progressSink.HandleError(sourceDirectory, destinationDirectory, ex, allowSkip: true);

                if (response == ExplorerTransferErrorAction.Retry)
                    continue;

                return response == ExplorerTransferErrorAction.Skip
                    ? OperationStepResult.Skipped
                    : OperationStepResult.Canceled;
            }
        }
    }

    private static ExplorerTransferSummary CreateTransferSummary(string destinationFolder, ExplorerTransferManifest manifest)
    {
        ExplorerTransferPreflight preflight = BuildTransferPreflight(
            manifest.SourcePaths,
            destinationFolder,
            manifest.Move);

        return new ExplorerTransferSummary
        {
            TotalBytes = preflight.TotalBytes,
            TotalItemCount = preflight.ItemCount,
            ConflictFileCount = preflight.ConflictItems.Count,
            IsSingleTopLevelFile = GetIsSingleTopLevelFile(manifest.SourcePaths),
            SourceFolderPath = GetTransferSourceFolder(manifest.SourcePaths),
            DestinationFolderPath = destinationFolder,
            ConflictItems = preflight.ConflictItems
        };
    }

    private static ExplorerTransferPreflight BuildTransferPreflight(
        IEnumerable<string> sourcePaths,
        string destinationFolder,
        bool move)
    {
        ExplorerTransferPreflight preflight = new();

        foreach (string sourcePath in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                continue;

            if (Directory.Exists(sourcePath))
            {
                AddDirectoryToPreflight(
                    sourcePath,
                    destinationFolder,
                    move,
                    collectConflicts: true,
                    preflight);
                continue;
            }

            if (File.Exists(sourcePath))
            {
                AddFileToPreflight(
                    sourcePath,
                    destinationFolder,
                    move,
                    collectConflicts: true,
                    preflight);
            }
        }

        return preflight;
    }

    private static void AddDirectoryToPreflight(
        string sourceDirectory,
        string destinationParentFolder,
        bool move,
        bool collectConflicts,
        ExplorerTransferPreflight preflight)
    {
        preflight.ItemCount++;

        if (IsDirectoryReparsePoint(sourceDirectory))
            return;

        string destinationDirectory = string.Empty;
        bool collectChildConflicts = false;

        if (collectConflicts && !PathEqualsOrIsDescendantOf(destinationParentFolder, sourceDirectory))
        {
            destinationDirectory = GetDestinationDirectoryPath(sourceDirectory, destinationParentFolder, move);
            collectChildConflicts = !PathsEqual(sourceDirectory, destinationDirectory) &&
                Directory.Exists(destinationDirectory);
        }

        foreach (string childDirectory in EnumerateDirectoriesSafe(sourceDirectory))
        {
            AddDirectoryToPreflight(
                childDirectory,
                destinationDirectory,
                move,
                collectChildConflicts,
                preflight);
        }

        foreach (string childFile in EnumerateFilesSafe(sourceDirectory))
        {
            AddFileToPreflight(
                childFile,
                destinationDirectory,
                move,
                collectChildConflicts,
                preflight);
        }
    }

    private static void AddFileToPreflight(
        string sourceFile,
        string destinationFolder,
        bool move,
        bool collectConflicts,
        ExplorerTransferPreflight preflight)
    {
        preflight.TotalBytes += GetFileLengthSafe(sourceFile);
        preflight.ItemCount++;

        if (!collectConflicts)
            return;

        string destinationPath = GetDestinationFilePath(sourceFile, destinationFolder, move);
        if (PathsEqual(sourceFile, destinationPath) || Directory.Exists(destinationPath))
            return;

        if (File.Exists(destinationPath))
            preflight.ConflictItems.Add(new ExplorerTransferConflictItem(sourceFile, destinationPath));
    }

    private static bool GetIsSingleTopLevelFile(IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths.Count != 1)
            return false;

        string sourcePath = sourcePaths[0];
        return !string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath);
    }

    private static string GetTransferSourceFolder(IReadOnlyList<string> sourcePaths)
    {
        List<string> existingPaths = [];

        foreach (string sourcePath in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                continue;

            if (File.Exists(sourcePath) || Directory.Exists(sourcePath))
                existingPaths.Add(sourcePath);
        }

        if (existingPaths.Count == 0)
            return string.Empty;

        string? firstParent = TryGetParentPath(existingPaths[0]);
        if (string.IsNullOrWhiteSpace(firstParent))
            return existingPaths[0];

        for (int i = 1; i < existingPaths.Count; i++)
        {
            string? parent = TryGetParentPath(existingPaths[i]);
            if (!PathsEqual(firstParent, parent))
                return firstParent;
        }

        return firstParent;
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string path)
    {
        try
        {
            return Directory.GetFiles(path);
        }
        catch
        {
            return [];
        }
    }

    private static void MoveFileDirect(
        string sourceFile,
        string destinationPath,
        bool overwriteExistingDestination)
    {
        if (overwriteExistingDestination)
        {
            File.Move(sourceFile, destinationPath, overwrite: true);
            return;
        }

        File.Move(sourceFile, destinationPath);
    }

    private static void CopyFileWithProgress(
        string sourceFile,
        string destinationPath,
        IExplorerTransferProgressSink progressSink,
        bool overwriteExistingDestination)
    {
        const int bufferSize = 1024 * 1024;

        long reportedBytes = 0;
        bool completed = false;
        string? temporaryDestinationPath = overwriteExistingDestination
            ? GetTemporaryOverwritePath(destinationPath)
            : null;
        string copyDestinationPath = temporaryDestinationPath ?? destinationPath;

        try
        {
            using (FileStream sourceStream = new(
                sourceFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize,
                FileOptions.SequentialScan))
            using (FileStream destinationStream = new(
                copyDestinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize,
                FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[bufferSize];
                int bytesRead;

                while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (progressSink.IsCancelled)
                        throw new OperationCanceledException();

                    destinationStream.Write(buffer, 0, bytesRead);
                    reportedBytes += bytesRead;
                    progressSink.AdjustCompletedBytes(bytesRead);
                }
            }

            ApplySourceFileMetadata(sourceFile, copyDestinationPath);

            if (temporaryDestinationPath != null)
            {
                File.Move(temporaryDestinationPath, destinationPath, overwrite: true);
                temporaryDestinationPath = null;
            }

            completed = true;
        }
        finally
        {
            if (!completed)
            {
                if (reportedBytes != 0)
                    progressSink.AdjustCompletedBytes(-reportedBytes);

                try
                {
                    string cleanupPath = temporaryDestinationPath ?? copyDestinationPath;
                    if (File.Exists(cleanupPath))
                        File.Delete(cleanupPath);
                }
                catch
                {
                }
            }
        }
    }

    private static void ApplySourceFileMetadata(string sourceFile, string destinationPath)
    {
        File.SetCreationTime(destinationPath, File.GetCreationTime(sourceFile));
        File.SetLastWriteTime(destinationPath, File.GetLastWriteTime(sourceFile));
        File.SetLastAccessTime(destinationPath, File.GetLastAccessTime(sourceFile));
        File.SetAttributes(destinationPath, File.GetAttributes(sourceFile));
    }

    private static bool TryDirectoryHasChildren(string path, out bool hasChildren)
    {
        hasChildren = false;

        try
        {
            using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            hasChildren = entries.MoveNext();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDirectoryReparsePoint(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) ==
                   (FileAttributes.Directory | FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static long GetFileLengthSafe(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static void MarkFileCompleted(string sourceFile, IExplorerTransferProgressSink progressSink)
    {
        MarkCompletedItems(GetFileLengthSafe(sourceFile), 1, progressSink);
    }

    private static void MarkDirectoryCompleted(string sourceDirectory, IExplorerTransferProgressSink progressSink)
    {
        (long totalBytes, long itemCount) = GetDirectoryMetricsSafe(sourceDirectory);
        MarkCompletedItems(totalBytes, itemCount, progressSink);
    }

    private static void MarkCompletedItems(long bytes, long itemCount, IExplorerTransferProgressSink progressSink)
    {
        progressSink.AdjustCompletedBytes(bytes);
        progressSink.AdjustCompletedItems(itemCount);
    }

    private static (long Bytes, long ItemCount) GetDirectoryMetricsSafe(string path)
    {
        long totalBytes = 0;
        long itemCount = 0;
        Stack<string> pendingDirectories = new();
        pendingDirectories.Push(path);

        while (pendingDirectories.Count != 0)
        {
            string currentDirectory = pendingDirectories.Pop();
            itemCount++;

            if (IsDirectoryReparsePoint(currentDirectory))
                continue;

            foreach (string childFile in EnumerateFilesSafe(currentDirectory))
            {
                totalBytes += GetFileLengthSafe(childFile);
                itemCount++;
            }

            foreach (string childDirectory in EnumerateDirectoriesSafe(currentDirectory))
                pendingDirectories.Push(childDirectory);
        }

        return (totalBytes, itemCount);
    }

    private static string GetDestinationDirectoryPath(string sourceDirectory, string destinationParentFolder, bool move)
    {
        string folderName = Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string? sourceParentFolder = TryGetParentPath(sourceDirectory);
        bool sameParent = PathsEqual(sourceParentFolder, destinationParentFolder);

        return move || !sameParent
            ? Path.Combine(destinationParentFolder, folderName)
            : GetSameFolderCopyPath(Path.Combine(destinationParentFolder, folderName), isDirectory: true);
    }

    private static string GetDestinationFilePath(string sourceFile, string destinationFolder, bool move)
    {
        string fileName = Path.GetFileName(sourceFile);
        string? sourceParentFolder = TryGetParentPath(sourceFile);
        bool sameParent = PathsEqual(sourceParentFolder, destinationFolder);

        return move || !sameParent
            ? Path.Combine(destinationFolder, fileName)
            : GetSameFolderCopyPath(Path.Combine(destinationFolder, fileName), isDirectory: false);
    }

    private static string GetSameFolderCopyPath(string originalPath, bool isDirectory)
    {
        string parent = Path.GetDirectoryName(originalPath) ?? string.Empty;
        string extension = isDirectory ? string.Empty : Path.GetExtension(originalPath);
        string nameWithoutExtension = isDirectory
            ? Path.GetFileName(originalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileNameWithoutExtension(originalPath);

        string firstCandidate = Path.Combine(parent, $"{nameWithoutExtension} - Copy{extension}");
        if (!File.Exists(firstCandidate) && !Directory.Exists(firstCandidate))
            return firstCandidate;

        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(parent, $"{nameWithoutExtension} - Copy ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    private static string GetNumberedConflictCopyPath(string originalPath)
    {
        string parent = Path.GetDirectoryName(originalPath) ?? string.Empty;
        string extension = Path.GetExtension(originalPath);
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);

        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(parent, $"{nameWithoutExtension} ({i}){extension}");

            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    private static string GetTemporaryOverwritePath(string destinationPath)
    {
        string parent = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        string fileName = Path.GetFileName(destinationPath);

        for (int i = 0; i < 20; i++)
        {
            string candidate = Path.Combine(parent, $".{fileName}.{Guid.NewGuid():N}.tmp");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        return Path.Combine(parent, Path.GetRandomFileName());
    }

    private static bool IsSameVolumePath(string sourcePath, string destinationPath)
    {
        try
        {
            string? sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
            string? destinationRoot = Path.GetPathRoot(Path.GetFullPath(destinationPath));

            return !string.IsNullOrWhiteSpace(sourceRoot) &&
                   !string.IsNullOrWhiteSpace(destinationRoot) &&
                   string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        static string Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathEqualsOrIsDescendantOf(string? path, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rootPath))
            return false;

        string normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetParentPath(string path)
    {
        try
        {
            return Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return null;
        }
    }

    private sealed class ExplorerTransferPreflight
    {
        public long TotalBytes { get; set; }
        public long ItemCount { get; set; }
        public List<ExplorerTransferConflictItem> ConflictItems { get; } = [];
    }

    private sealed class TransferChangeSet
    {
        private readonly HashSet<string> _fileChangedParents = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _folderChildrenChangedParents = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string OldPath, string NewPath)> _folderRelocations = [];

        public void RecordFileChanged(string parentFolderPath)
        {
            if (!string.IsNullOrWhiteSpace(parentFolderPath))
                _fileChangedParents.Add(parentFolderPath);
        }

        public void RecordFolderChildrenChanged(string parentFolderPath)
        {
            if (!string.IsNullOrWhiteSpace(parentFolderPath))
                _folderChildrenChangedParents.Add(parentFolderPath);
        }

        public void RecordFolderRelocated(string oldPath, string newPath)
        {
            if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
                return;

            _folderRelocations.Add((oldPath, newPath));
        }

        public void Apply(IFileSystemChangeNotifier changeNotifier, RefreshReason reason)
        {
            foreach (string parentFolderPath in _folderChildrenChangedParents)
                changeNotifier.NotifyFolderChildrenChanged(parentFolderPath, reason);

            foreach (string parentFolderPath in _fileChangedParents)
            {
                if (!_folderChildrenChangedParents.Contains(parentFolderPath))
                    changeNotifier.NotifyFileChanged(parentFolderPath, reason);
            }

            foreach ((string oldPath, string newPath) in _folderRelocations)
                changeNotifier.NotifyFolderRelocated(oldPath, newPath, reason);
        }
    }
}
