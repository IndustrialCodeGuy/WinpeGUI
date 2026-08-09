using SharedDriveSnapshot = Shared.Shell.Models.DriveSnapshot;
using Shell.Core.Models;

namespace Shell.Core.Interfaces;

public interface IExplorerWindow
{
    string WindowId { get; }

    ExplorerWindowState GetWindowState();
    ExplorerWindowPlacement? GetWindowPlacement();

    void ApplyDriveSetSnapshot(DriveSetSnapshot snapshot, RefreshReason reason);
    void ApplyDriveSnapshot(SharedDriveSnapshot snapshot, RefreshReason reason);

    void RequestRefreshCurrentView(RefreshReason reason);
    void NavigateToPath(string path);
    void ReloadTreeDriveRoots();
    void RefreshLoadedTreeFolderChildren(string parentPath);
    void RetargetCurrentPath(string oldPath, string newPath);
    void RetargetDeletedPath(string deletedPath, string fallbackPath);

    void ActivateWindow();
}