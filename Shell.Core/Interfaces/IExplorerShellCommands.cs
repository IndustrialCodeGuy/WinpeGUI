using Shell.Core.FileTypes;
using Shared.Shell.Models;
using Shell.Core.Models;

namespace Shell.Core.Interfaces;

public enum ExplorerBitLockerAction
{
    Manage,
    Unlock
}

public interface IExplorerShellCommands
{
    bool CanUseExplorerBitLockerUi { get; }
    bool CanEjectDriveDevice(string driveRoot);

    void ShowDriveNotReadyMessage(
        string driveRoot,
        DriveIssueKind? issueKind = null,
        string? issueMessage = null);

    void ShowOpticalDriveEmptyMessage(string driveRoot);
    void OpenNewWindow(
        string? initialPath = null,
        ExplorerPreloadedDirectoryListing? preloadedDirectoryListing = null);
    void RefreshWindow(string windowId);
    void EditFileInNotepad(string path);
    void OpenFileSystemItem(string path);

    void ExecuteFileOpenCommand(
        string path,
        ExplorerOpenCommand command,
        string dialogTitle);

    void CopyPathsToClipboard(IReadOnlyList<string> paths);
    void OpenItemProperties(string path);

    void LaunchBitLockerHelper(
        string driveRoot,
        ExplorerBitLockerAction action,
        string? navigateAfterUnlockPath = null,
        string? navigationWindowId = null,
        bool openInNewWindowAfterUnlock = false);

    string? CreateNewFolder(string parentFolderPath);
    void DeletePaths(IReadOnlyList<string> paths);
    bool RenameFileSystemEntry(string path, bool isDirectory, string newName);
    bool RenameDriveLabel(string rootPath, string newLabel);
    void SetClipboardFileTransfer(IReadOnlyList<string> sourcePaths, bool move);
    bool ClearClipboard();
    bool CanPasteFileTransfer();
    void PasteFileTransfer(string destinationFolder);
    void OpenItemWith(string path);
    void FormatDrive(string driveRoot);
    void EjectOrDisconnectDrive(string driveRoot);
}