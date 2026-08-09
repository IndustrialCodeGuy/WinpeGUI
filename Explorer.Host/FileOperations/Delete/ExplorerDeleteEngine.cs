using Shell.Core.Interfaces;
using Shell.Core.Models;

namespace Explorer.Host.FileOperations.Delete;

internal static class ExplorerDeleteEngine
{
    private enum OperationStepResult
    {
        Completed,
        Skipped,
        Canceled
    }

    private sealed class DeleteFailure
    {
        public DeleteFailure(
            string sourcePath,
            Exception exception,
            Action retryAction,
            bool tryBeforePrompt,
            long progressBytes,
            long progressItemCount)
        {
            SourcePath = sourcePath;
            Exception = exception;
            RetryAction = retryAction;
            TryBeforePrompt = tryBeforePrompt;
            ProgressBytes = Math.Max(0, progressBytes);
            ProgressItemCount = Math.Max(0, progressItemCount);
        }

        public string SourcePath { get; }
        public Exception Exception { get; set; }
        public Action RetryAction { get; }
        public bool TryBeforePrompt { get; }
        public long ProgressBytes { get; }
        public long ProgressItemCount { get; }
    }

    public static bool ExecuteDelete(
        IReadOnlyList<string> sourcePaths,
        IExplorerDeleteProgressSink progressSink,
        IFileSystemChangeNotifier notifier,
        RefreshReason reason)
    {
        if (sourcePaths == null || sourcePaths.Count == 0)
            return false;

        HashSet<string> fileParents = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> folderParents = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> sourceDirectoryRoots = new(StringComparer.OrdinalIgnoreCase);
        List<DeleteFailure> deferredFailures = [];
        List<string> deferredDirectoryCleanup = [];

        try
        {
            (long totalBytes, long totalItemCount) = CalculateProgressTotals(sourcePaths);
            progressSink.InitializeProgress(totalBytes, totalItemCount);

            foreach (string sourcePath in sourcePaths)
            {
                if (progressSink.IsCancelled)
                    return false;

                if (string.IsNullOrWhiteSpace(sourcePath))
                    continue;

                bool sourceWasDirectory =
                    TryGetAttributes(sourcePath, out FileAttributes sourceAttributes) &&
                    (sourceAttributes & FileAttributes.Directory) != 0;

                if (sourceWasDirectory)
                    sourceDirectoryRoots.Add(sourcePath);

                OperationStepResult result = DeletePathEntry(
                    sourcePath,
                    progressSink,
                    fileParents,
                    folderParents,
                    deferredFailures,
                    deferredDirectoryCleanup,
                    deferErrors: true);

                if (result == OperationStepResult.Canceled)
                    return false;
            }

            if (deferredFailures.Count > 0 && HasAccumulatedDeleteChanges(fileParents, folderParents, sourceDirectoryRoots))
            {
                NotifyAccumulatedDeleteChanges(
                    notifier,
                    reason,
                    fileParents,
                    folderParents,
                    sourceDirectoryRoots);
            }

            OperationStepResult failureResult = ProcessDeferredFailures(
                deferredFailures,
                progressSink);

            if (failureResult == OperationStepResult.Canceled)
                return false;

            OperationStepResult cleanupResult = RunDeferredDirectoryCleanup(
                deferredDirectoryCleanup,
                progressSink,
                folderParents);

            if (cleanupResult == OperationStepResult.Canceled)
                return false;

            progressSink.CompleteProgress();
            return true;
        }
        finally
        {
            NotifyAccumulatedDeleteChanges(
                notifier,
                reason,
                fileParents,
                folderParents,
                sourceDirectoryRoots);
        }
    }

    private static bool HasAccumulatedDeleteChanges(
        HashSet<string> fileParents,
        HashSet<string> folderParents,
        HashSet<string> sourceDirectoryRoots)
    {
        if (fileParents.Count > 0 || folderParents.Count > 0)
            return true;

        foreach (string sourceDirectoryRoot in sourceDirectoryRoots)
        {
            if (!Directory.Exists(sourceDirectoryRoot))
                return true;
        }

        return false;
    }

    private static void NotifyAccumulatedDeleteChanges(
        IFileSystemChangeNotifier notifier,
        RefreshReason reason,
        HashSet<string> fileParents,
        HashSet<string> folderParents,
        HashSet<string> sourceDirectoryRoots)
    {
        HashSet<string> deletedFolderRoots = new(StringComparer.OrdinalIgnoreCase);

        foreach (string sourceDirectoryRoot in sourceDirectoryRoots)
        {
            if (!Directory.Exists(sourceDirectoryRoot))
                deletedFolderRoots.Add(sourceDirectoryRoot);
        }

        string[] deletedFolderPaths = GetDistinctDeletedFolderRoots(deletedFolderRoots).ToArray();

        foreach (string deletedFolderPath in deletedFolderPaths)
            notifier.NotifyFolderDeleted(deletedFolderPath, reason);

        foreach (string parentPath in folderParents)
        {
            if (!IsChangeCoveredByDeletedFolder(parentPath, deletedFolderPaths))
                notifier.NotifyFolderChildrenChanged(parentPath, reason);
        }

        foreach (string parentPath in fileParents)
        {
            if (folderParents.Contains(parentPath) ||
                IsChangeCoveredByDeletedFolder(parentPath, deletedFolderPaths))
            {
                continue;
            }

            notifier.NotifyFileChanged(parentPath, reason);
        }
    }

    private static OperationStepResult DeletePathEntry(
        string path,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> fileParents,
        HashSet<string> folderParents,
        List<DeleteFailure> deferredFailures,
        List<string> deferredDirectoryCleanup,
        bool deferErrors)
    {
        if (string.IsNullOrWhiteSpace(path))
            return OperationStepResult.Completed;

        FileAttributes attributes;

        while (!TryGetAttributes(path, out attributes, out Exception? attributeException))
        {
            if (IsPathNotFoundException(attributeException))
                return OperationStepResult.Completed;

            Exception failureException = attributeException ??
                new IOException("The file or folder attributes could not be read.");

            if (deferErrors)
            {
                deferredFailures.Add(new DeleteFailure(
                    path,
                    failureException,
                    () => RetryDeletePathEntry(path, progressSink, fileParents, folderParents),
                    tryBeforePrompt: false,
                    progressBytes: 0,
                    progressItemCount: 0));

                return OperationStepResult.Skipped;
            }

            ExplorerDeleteErrorAction response = progressSink.HandleError(path, failureException);

            if (response == ExplorerDeleteErrorAction.Retry)
                continue;

            return response == ExplorerDeleteErrorAction.Skip
                ? OperationStepResult.Skipped
                : OperationStepResult.Canceled;
        }

        return DeletePathEntryWithAttributes(
            path,
            attributes,
            progressSink,
            fileParents,
            folderParents,
            deferredFailures,
            deferredDirectoryCleanup,
            deferErrors);
    }

    private static OperationStepResult DeletePathEntryWithAttributes(
        string path,
        FileAttributes attributes,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> fileParents,
        HashSet<string> folderParents,
        List<DeleteFailure> deferredFailures,
        List<string> deferredDirectoryCleanup,
        bool deferErrors)
    {
        bool isDirectory = (attributes & FileAttributes.Directory) != 0;

        if (isDirectory)
        {
            if (!Directory.Exists(path))
                return OperationStepResult.Completed;

            if (IsReparsePoint(attributes))
            {
                return DeleteDirectoryObject(
                    path,
                    progressSink,
                    folderParents,
                    deferredFailures,
                    deferErrors);
            }

            return DeleteDirectoryEntry(
                path,
                progressSink,
                fileParents,
                folderParents,
                deferredFailures,
                deferredDirectoryCleanup,
                deferErrors);
        }

        if (!File.Exists(path))
            return OperationStepResult.Completed;

        return DeleteFileEntry(
            path,
            progressSink,
            fileParents,
            deferredFailures,
            deferErrors);
    }

    private static OperationStepResult DeleteDirectoryEntry(
        string sourceDirectory,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> fileParents,
        HashSet<string> folderParents,
        List<DeleteFailure> deferredFailures,
        List<string> deferredDirectoryCleanup,
        bool deferErrors)
    {
        string[] childDirectories = GetDirectoriesWithRetrySkipCancel(
            sourceDirectory,
            progressSink,
            fileParents,
            folderParents,
            deferredFailures,
            deferredDirectoryCleanup,
            deferErrors,
            out OperationStepResult dirListResult);

        if (dirListResult == OperationStepResult.Canceled)
            return OperationStepResult.Canceled;

        bool skippedSomething = dirListResult == OperationStepResult.Skipped;

        foreach (string dir in childDirectories)
        {
            OperationStepResult childResult = DeleteChildPath(
                dir,
                progressSink,
                fileParents,
                folderParents,
                deferredFailures,
                deferredDirectoryCleanup,
                deferErrors);

            if (childResult == OperationStepResult.Canceled)
                return OperationStepResult.Canceled;

            if (childResult == OperationStepResult.Skipped)
                skippedSomething = true;
        }

        string[] childFiles = GetFilesWithRetrySkipCancel(
            sourceDirectory,
            progressSink,
            fileParents,
            folderParents,
            deferredFailures,
            deferredDirectoryCleanup,
            deferErrors,
            out OperationStepResult fileListResult);

        if (fileListResult == OperationStepResult.Canceled)
            return OperationStepResult.Canceled;

        if (fileListResult == OperationStepResult.Skipped)
            skippedSomething = true;

        foreach (string file in childFiles)
        {
            OperationStepResult childResult = DeleteChildPath(
                file,
                progressSink,
                fileParents,
                folderParents,
                deferredFailures,
                deferredDirectoryCleanup,
                deferErrors);

            if (childResult == OperationStepResult.Canceled)
                return OperationStepResult.Canceled;

            if (childResult == OperationStepResult.Skipped)
                skippedSomething = true;
        }

        progressSink.ReportProgress("Deleting folder...", sourceDirectory);

        if (skippedSomething)
        {
            AddDeferredDirectoryCleanup(deferredDirectoryCleanup, sourceDirectory);
            return OperationStepResult.Skipped;
        }

        return DeleteDirectoryObjectCore(
            sourceDirectory,
            progressSink,
            folderParents,
            deferredFailures,
            deferErrors);
    }

    private static OperationStepResult DeleteChildPath(
        string childPath,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> fileParents,
        HashSet<string> folderParents,
        List<DeleteFailure> deferredFailures,
        List<string> deferredDirectoryCleanup,
        bool deferErrors)
    {
        if (progressSink.IsCancelled)
            return OperationStepResult.Canceled;

        return DeletePathEntry(
            childPath,
            progressSink,
            fileParents,
            folderParents,
            deferredFailures,
            deferredDirectoryCleanup,
            deferErrors);
    }

    private static OperationStepResult DeleteDirectoryObjectCore(
        string sourceDirectory,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> folderParents,
        List<DeleteFailure> deferredFailures,
        bool deferErrors)
    {
        progressSink.ReportProgress("Deleting folder...", sourceDirectory);

        Action deleteDirectory = () =>
        {
            Directory.Delete(sourceDirectory, recursive: false);
            AddParentPath(folderParents, sourceDirectory);
        };

        if (deferErrors)
        {
            OperationStepResult deferredResult = RunOrDefer(
                sourceDirectory,
                deleteDirectory,
                () =>
                {
                    progressSink.ReportProgress("Deleting folder...", sourceDirectory);
                    deleteDirectory();
                    progressSink.AdjustCompletedItems(1);
                },
                deferredFailures,
                tryBeforePrompt: true,
                progressBytes: 0,
                progressItemCount: 1);

            if (deferredResult == OperationStepResult.Completed)
                progressSink.AdjustCompletedItems(1);

            return deferredResult;
        }

        OperationStepResult deleteResult = RunWithRetrySkipCancel(
            sourceDirectory,
            deleteDirectory,
            progressSink,
            allowSkip: true);

        switch (deleteResult)
        {
            case OperationStepResult.Completed:
            case OperationStepResult.Skipped:
                progressSink.AdjustCompletedItems(1);
                break;
        }

        return deleteResult;
    }

    private static OperationStepResult DeleteDirectoryObject(
        string sourceDirectory,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> folderParents,
        List<DeleteFailure> deferredFailures,
        bool deferErrors)
    {
        return DeleteDirectoryObjectCore(
            sourceDirectory,
            progressSink,
            folderParents,
            deferredFailures,
            deferErrors);
    }

    private static OperationStepResult DeleteFileEntry(
        string sourceFile,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> fileParents,
        List<DeleteFailure> deferredFailures,
        bool deferErrors)
    {
        long fileLength = GetFileLengthSafe(sourceFile);

        progressSink.ReportProgress("Deleting file...", sourceFile);

        Action deleteFile = () =>
        {
            File.Delete(sourceFile);
            AddParentPath(fileParents, sourceFile);
        };

        if (deferErrors)
        {
            OperationStepResult deferredResult = RunOrDefer(
                sourceFile,
                deleteFile,
                () =>
                {
                    progressSink.ReportProgress("Deleting file...", sourceFile);
                    deleteFile();
                    progressSink.AdjustCompletedBytes(fileLength);
                    progressSink.AdjustCompletedItems(1);
                },
                deferredFailures,
                tryBeforePrompt: true,
                progressBytes: fileLength,
                progressItemCount: 1);

            if (deferredResult == OperationStepResult.Completed)
            {
                progressSink.AdjustCompletedBytes(fileLength);
                progressSink.AdjustCompletedItems(1);
            }

            return deferredResult;
        }

        OperationStepResult deleteResult = RunWithRetrySkipCancel(
            sourceFile,
            deleteFile,
            progressSink,
            allowSkip: true);

        switch (deleteResult)
        {
            case OperationStepResult.Completed:
            case OperationStepResult.Skipped:
                progressSink.AdjustCompletedBytes(fileLength);
                progressSink.AdjustCompletedItems(1);
                break;
        }

        return deleteResult;
    }

    private static OperationStepResult RunOrDefer(
        string sourcePath,
        Action action,
        Action retryAction,
        List<DeleteFailure> deferredFailures,
        bool tryBeforePrompt,
        long progressBytes,
        long progressItemCount)
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
            deferredFailures.Add(new DeleteFailure(
                sourcePath,
                ex,
                retryAction,
                tryBeforePrompt,
                progressBytes,
                progressItemCount));

            return OperationStepResult.Skipped;
        }
    }

    private static OperationStepResult ProcessDeferredFailures(
        List<DeleteFailure> deferredFailures,
        IExplorerDeleteProgressSink progressSink)
    {
        foreach (DeleteFailure failure in deferredFailures)
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

                ExplorerDeleteErrorAction response = progressSink.HandleError(
                    failure.SourcePath,
                    failure.Exception);

                if (response == ExplorerDeleteErrorAction.Skip)
                {
                    progressSink.AdjustCompletedBytes(failure.ProgressBytes);
                    progressSink.AdjustCompletedItems(failure.ProgressItemCount);
                    break;
                }

                if (response == ExplorerDeleteErrorAction.Cancel)
                    return OperationStepResult.Canceled;

                if (TryRetryFailure(failure, out Exception? retryException))
                    break;

                failure.Exception = retryException ?? failure.Exception;
            }
        }

        return OperationStepResult.Completed;
    }

    private static bool TryRetryFailure(DeleteFailure failure, out Exception? exception)
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

    private static OperationStepResult RunDeferredDirectoryCleanup(
        List<string> deferredDirectoryCleanup,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> folderParents)
    {
        foreach (string directoryPath in deferredDirectoryCleanup
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(static path => path.Length))
        {
            if (progressSink.IsCancelled)
                return OperationStepResult.Canceled;

            if (!Directory.Exists(directoryPath))
                continue;

            progressSink.ReportProgress("Deleting folder...", directoryPath);

            try
            {
                Directory.Delete(directoryPath, recursive: false);
                AddParentPath(folderParents, directoryPath);
                progressSink.AdjustCompletedItems(1);
            }
            catch
            {
                // The folder can remain when the user skipped a failed child item.
            }
        }

        return OperationStepResult.Completed;
    }

    private static void AddDeferredDirectoryCleanup(
        List<string> deferredDirectoryCleanup,
        string sourceDirectory)
    {
        if (!string.IsNullOrWhiteSpace(sourceDirectory))
            deferredDirectoryCleanup.Add(sourceDirectory);
    }

    private static void RetryDeletePathEntry(
        string sourcePath,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> fileParents,
        HashSet<string> folderParents)
    {
        if (!TryGetAttributes(sourcePath, out FileAttributes attributes, out Exception? attributeException))
        {
            if (IsPathNotFoundException(attributeException))
                return;

            throw attributeException ?? new IOException("The file or folder attributes could not be read.");
        }

        OperationStepResult result = DeletePathEntryWithAttributes(
            sourcePath,
            attributes,
            progressSink,
            fileParents,
            folderParents,
            [],
            [],
            deferErrors: false);

        if (result == OperationStepResult.Canceled)
            throw new OperationCanceledException();

        if (result != OperationStepResult.Skipped)
            return;

        if (!TryGetAttributes(sourcePath, out FileAttributes remainingAttributes, out Exception? remainingException))
        {
            if (IsPathNotFoundException(remainingException))
                return;

            throw remainingException ?? new IOException("The file or folder attributes could not be read.");
        }

        bool isDirectory = (remainingAttributes & FileAttributes.Directory) != 0;
        throw new IOException(isDirectory
            ? "The folder could not be deleted."
            : "The file could not be deleted.");
    }

    private static OperationStepResult RunWithRetrySkipCancel(
        string sourcePath,
        Action action,
        IExplorerDeleteProgressSink progressSink,
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
                ExplorerDeleteErrorAction response = progressSink.HandleError(sourcePath, ex);

                if (response == ExplorerDeleteErrorAction.Retry)
                    continue;

                if (response == ExplorerDeleteErrorAction.Skip && allowSkip)
                    return OperationStepResult.Skipped;

                return OperationStepResult.Canceled;
            }
        }
    }

    private static string[] GetDirectoriesWithRetrySkipCancel(
        string directoryPath,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> fileParents,
        HashSet<string> folderParents,
        List<DeleteFailure> deferredFailures,
        List<string> deferredDirectoryCleanup,
        bool deferErrors,
        out OperationStepResult result)
    {
        return GetDirectoryEntriesWithRetrySkipCancel(
            directoryPath,
            Directory.GetDirectories,
            progressSink,
            fileParents,
            folderParents,
            deferredFailures,
            deferredDirectoryCleanup,
            deferErrors,
            out result);
    }

    private static string[] GetFilesWithRetrySkipCancel(
        string directoryPath,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> fileParents,
        HashSet<string> folderParents,
        List<DeleteFailure> deferredFailures,
        List<string> deferredDirectoryCleanup,
        bool deferErrors,
        out OperationStepResult result)
    {
        return GetDirectoryEntriesWithRetrySkipCancel(
            directoryPath,
            Directory.GetFiles,
            progressSink,
            fileParents,
            folderParents,
            deferredFailures,
            deferredDirectoryCleanup,
            deferErrors,
            out result);
    }

    private static string[] GetDirectoryEntriesWithRetrySkipCancel(
        string directoryPath,
        Func<string, string[]> getEntries,
        IExplorerDeleteProgressSink progressSink,
        HashSet<string> fileParents,
        HashSet<string> folderParents,
        List<DeleteFailure> deferredFailures,
        List<string> deferredDirectoryCleanup,
        bool deferErrors,
        out OperationStepResult result)
    {
        while (true)
        {
            if (progressSink.IsCancelled)
            {
                result = OperationStepResult.Canceled;
                return [];
            }

            try
            {
                result = OperationStepResult.Completed;
                return getEntries(directoryPath);
            }
            catch (Exception ex)
            {
                if (deferErrors)
                {
                    deferredFailures.Add(new DeleteFailure(
                        directoryPath,
                        ex,
                        () => RetryDeletePathEntry(directoryPath, progressSink, fileParents, folderParents),
                        tryBeforePrompt: false,
                        progressBytes: 0,
                        progressItemCount: 0));

                    AddDeferredDirectoryCleanup(deferredDirectoryCleanup, directoryPath);
                    result = OperationStepResult.Skipped;
                    return [];
                }

                ExplorerDeleteErrorAction response = progressSink.HandleError(directoryPath, ex);

                if (response == ExplorerDeleteErrorAction.Retry)
                    continue;

                if (response == ExplorerDeleteErrorAction.Skip)
                {
                    result = OperationStepResult.Skipped;
                    return [];
                }

                result = OperationStepResult.Canceled;
                return [];
            }
        }
    }

    private static (long TotalBytes, long TotalItemCount) CalculateProgressTotals(IReadOnlyList<string> sourcePaths)
    {
        long totalBytes = 0;
        long totalItemCount = 0;

        foreach (string sourcePath in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                continue;

            (long pathBytes, long pathItems) = CalculatePathTotals(sourcePath);
            totalBytes += pathBytes;
            totalItemCount += pathItems;
        }

        return (totalBytes, totalItemCount);
    }

    private static (long TotalBytes, long TotalItemCount) CalculatePathTotals(string path)
    {
        if (!TryGetAttributes(path, out FileAttributes attributes))
            return (0, 0);

        bool isDirectory = (attributes & FileAttributes.Directory) != 0;

        if (isDirectory)
        {
            if (!Directory.Exists(path))
                return (0, 0);

            if (IsReparsePoint(attributes))
                return (0, 1);

            (long childBytes, long childItems) = CalculateDirectoryTotals(path);
            return (childBytes, childItems + 1);
        }

        if (!File.Exists(path))
            return (0, 0);

        return (GetFileLengthSafe(path), 1);
    }

    private static (long TotalBytes, long TotalItemCount) CalculateDirectoryTotals(string sourceDirectory)
    {
        long totalBytes = 0;
        long totalItemCount = 0;

        try
        {
            foreach (string dir in Directory.GetDirectories(sourceDirectory))
            {
                (long childBytes, long childItems) = CalculatePathTotals(dir);
                totalBytes += childBytes;
                totalItemCount += childItems;
            }
        }
        catch
        {
        }

        try
        {
            foreach (string file in Directory.GetFiles(sourceDirectory))
            {
                if (!File.Exists(file))
                    continue;

                totalBytes += GetFileLengthSafe(file);
                totalItemCount++;
            }
        }
        catch
        {
        }

        return (totalBytes, totalItemCount);
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        return TryGetAttributes(path, out attributes, out _);
    }

    private static bool TryGetAttributes(
        string path,
        out FileAttributes attributes,
        out Exception? exception)
    {
        try
        {
            attributes = File.GetAttributes(path);
            exception = null;
            return true;
        }
        catch (Exception ex)
        {
            attributes = default;
            exception = ex;
            return false;
        }
    }

    private static bool IsPathNotFoundException(Exception? exception)
    {
        return exception is FileNotFoundException or DirectoryNotFoundException;
    }

    private static bool IsReparsePoint(FileAttributes attributes)
    {
        return (attributes & FileAttributes.ReparsePoint) != 0;
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

    private static void AddParentPath(HashSet<string> parents, string path)
    {
        string? parentPath = Path.GetDirectoryName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (!string.IsNullOrWhiteSpace(parentPath))
            parents.Add(parentPath);
    }

    private static bool IsChangeCoveredByDeletedFolder(
        string changedParentPath,
        IReadOnlyList<string> deletedFolderPaths)
    {
        if (string.IsNullOrWhiteSpace(changedParentPath) || deletedFolderPaths.Count == 0)
            return false;

        foreach (string deletedFolderPath in deletedFolderPaths)
        {
            if (PathEqualsOrIsDescendantOf(changedParentPath, deletedFolderPath))
                return true;

            string? deletedParentPath = TryGetParentPath(deletedFolderPath);
            if (!string.IsNullOrWhiteSpace(deletedParentPath) &&
                PathsEqual(changedParentPath, deletedParentPath))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetParentPath(string path)
    {
        try
        {
            return Path.GetDirectoryName(path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return null;
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        return string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetDistinctDeletedFolderRoots(IEnumerable<string> deletedFolderRoots)
    {
        List<string> ordered = deletedFolderRoots
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(static path => path.Length)
            .ToList();

        List<string> collapsed = [];

        foreach (string candidate in ordered)
        {
            if (collapsed.Any(existing => PathEqualsOrIsDescendantOf(candidate, existing)))
                continue;

            collapsed.Add(candidate);
        }

        return collapsed;
    }

    private static bool PathEqualsOrIsDescendantOf(string path, string rootPath)
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
}
