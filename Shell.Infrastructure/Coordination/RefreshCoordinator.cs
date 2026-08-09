using Shell.Core.Interfaces;
using Shell.Core.Models;
using SharedDriveSnapshot = Shared.Shell.Models.DriveSnapshot;

namespace Shell.Infrastructure.Coordination;

public sealed class RefreshCoordinator
{
    private readonly IDriveStateStore _driveStateStore;
    private readonly IExplorerWindowRegistry _windowRegistry;

    public RefreshCoordinator(
        IDriveStateStore driveStateStore,
        IExplorerWindowRegistry windowRegistry)
    {
        _driveStateStore = driveStateStore;
        _windowRegistry = windowRegistry;
    }

    public void HandleTopologyChanged(RefreshReason reason)
    {
        IReadOnlyList<IExplorerWindow> windows = _windowRegistry.GetAllWindows();

        DriveSetSnapshot previous = _driveStateStore.GetCurrentSnapshot();
        DriveSetSnapshot snapshot = _driveStateStore.RefreshAll();

        ApplySnapshotToWindows(windows, snapshot, reason);

        HashSet<string> changedRoots = GetChangedDriveRoots(previous, snapshot);
        if (changedRoots.Count == 0)
            return;

        foreach (IExplorerWindow window in windows)
        {
            ExplorerWindowState state = window.GetWindowState();
            if (!string.IsNullOrWhiteSpace(state.CurrentDriveRoot) &&
                changedRoots.Contains(state.CurrentDriveRoot))
            {
                window.RequestRefreshCurrentView(reason);
            }
        }
    }

    public void HandleDriveStatesChanged(IReadOnlyList<string> driveRoots, RefreshReason reason)
    {
        IReadOnlyList<IExplorerWindow> windows = _windowRegistry.GetAllWindows();
        DriveSetSnapshot snapshot = _driveStateStore.RebuildFromCurrentSource();
        ApplySnapshotToWindows(windows, snapshot, reason);

        if (driveRoots.Count == 0)
        {
            RequestCurrentViewRefresh(windows, reason);
            return;
        }

        HashSet<string> affectedRoots = new(
            driveRoots.Where(static root => !string.IsNullOrWhiteSpace(root)),
            StringComparer.OrdinalIgnoreCase);

        if (affectedRoots.Count == 0)
            return;

        foreach (IExplorerWindow window in windows)
        {
            ExplorerWindowState state = window.GetWindowState();
            if (!string.IsNullOrWhiteSpace(state.CurrentDriveRoot) &&
                affectedRoots.Contains(state.CurrentDriveRoot))
            {
                window.RequestRefreshCurrentView(reason);
            }
        }
    }

    public void HandleDriveStateChanged(string driveRoot, RefreshReason reason)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
            return;

        IReadOnlyList<IExplorerWindow> windows = _windowRegistry.GetAllWindows();
        DriveSetSnapshot snapshot = _driveStateStore.RebuildFromCurrentSource();

        SharedDriveSnapshot? driveSnapshot = snapshot.Drives.FirstOrDefault(d =>
            string.Equals(d.DriveRoot, driveRoot, StringComparison.OrdinalIgnoreCase));

        if (driveSnapshot is not null)
            ApplyDriveToWindows(windows, driveSnapshot, reason);
        else
            ApplySnapshotToWindows(windows, snapshot, reason);

        if (reason != RefreshReason.BitLockerStateChanged)
            return;

        foreach (IExplorerWindow window in windows)
        {
            ExplorerWindowState state = window.GetWindowState();
            if (string.Equals(state.CurrentDriveRoot, driveRoot, StringComparison.OrdinalIgnoreCase))
                window.RequestRefreshCurrentView(reason);
        }
    }

    public void HandleManualRefresh(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;

        IExplorerWindow? window = _windowRegistry.GetAllWindows().FirstOrDefault(w =>
            string.Equals(w.WindowId, windowId, StringComparison.Ordinal));

        window?.RequestRefreshCurrentView(RefreshReason.ManualRefresh);
    }

    public void HandleAllWindowsRefresh(RefreshReason reason)
    {
        IReadOnlyList<IExplorerWindow> windows = _windowRegistry.GetAllWindows();
        DriveSetSnapshot snapshot = _driveStateStore.RefreshAll();
        ApplySnapshotToWindows(windows, snapshot, reason);
        RequestCurrentViewRefresh(windows, reason);
    }

    public void HandleFileChanged(string parentFolderPath, RefreshReason reason)
    {
        if (string.IsNullOrWhiteSpace(parentFolderPath))
            return;

        foreach (IExplorerWindow window in _windowRegistry.GetAllWindows())
        {
            ExplorerWindowState state = window.GetWindowState();
            if (PathsEqual(state.CurrentPath, parentFolderPath))
                window.RequestRefreshCurrentView(reason);
        }
    }

    public void HandleFolderChildrenChanged(string parentFolderPath, RefreshReason reason)
    {
        if (string.IsNullOrWhiteSpace(parentFolderPath))
            return;

        foreach (IExplorerWindow window in _windowRegistry.GetAllWindows())
        {
            ExplorerWindowState state = window.GetWindowState();
            if (PathsEqual(state.CurrentPath, parentFolderPath))
                window.RequestRefreshCurrentView(reason);

            window.RefreshLoadedTreeFolderChildren(parentFolderPath);
        }
    }

    public void HandleFolderRelocated(string oldPath, string newPath, RefreshReason reason)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
            return;

        string? oldParent = TryGetParentPath(oldPath);
        string? newParent = TryGetParentPath(newPath);

        foreach (IExplorerWindow window in _windowRegistry.GetAllWindows())
        {
            ExplorerWindowState state = window.GetWindowState();
            bool refreshedCurrentView = false;

            if (PathEqualsOrIsDescendantOf(state.CurrentPath, oldPath))
            {
                window.RetargetCurrentPath(oldPath, newPath);
                window.RequestRefreshCurrentView(reason);
                refreshedCurrentView = true;
            }

            if (!string.IsNullOrWhiteSpace(oldParent))
            {
                if (!refreshedCurrentView && PathsEqual(state.CurrentPath, oldParent))
                {
                    window.RequestRefreshCurrentView(reason);
                    refreshedCurrentView = true;
                }

                window.RefreshLoadedTreeFolderChildren(oldParent);
            }

            if (!string.IsNullOrWhiteSpace(newParent) &&
                !PathsEqual(oldParent, newParent))
            {
                if (!refreshedCurrentView && PathsEqual(state.CurrentPath, newParent))
                {
                    window.RequestRefreshCurrentView(reason);
                    refreshedCurrentView = true;
                }

                window.RefreshLoadedTreeFolderChildren(newParent);
            }
        }
    }

    public void HandleFolderDeleted(string deletedFolderPath, RefreshReason reason)
    {
        if (string.IsNullOrWhiteSpace(deletedFolderPath))
            return;

        string fallbackPath = ResolveDeletedFolderFallbackPath(deletedFolderPath);
        string? deletedParent = TryGetParentPath(deletedFolderPath);

        foreach (IExplorerWindow window in _windowRegistry.GetAllWindows())
        {
            ExplorerWindowState state = window.GetWindowState();
            bool refreshedCurrentView = false;

            if (PathEqualsOrIsDescendantOf(state.CurrentPath, deletedFolderPath))
            {
                window.RetargetDeletedPath(deletedFolderPath, fallbackPath);
                window.RequestRefreshCurrentView(reason);
                refreshedCurrentView = true;
            }

            if (!string.IsNullOrWhiteSpace(deletedParent))
            {
                if (!refreshedCurrentView && PathsEqual(state.CurrentPath, deletedParent))
                    window.RequestRefreshCurrentView(reason);

                window.RefreshLoadedTreeFolderChildren(deletedParent);
            }
        }
    }

    private static string ResolveDeletedFolderFallbackPath(string deletedFolderPath)
    {
        string? candidate = TryGetParentPath(deletedFolderPath);

        while (!string.IsNullOrWhiteSpace(candidate))
        {
            try
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }
            catch
            {
            }

            candidate = TryGetParentPath(candidate);
        }

        try
        {
            string? root = Path.GetPathRoot(deletedFolderPath);
            if (!string.IsNullOrWhiteSpace(root))
                return root;
        }
        catch
        {
        }

        return deletedFolderPath;
    }

    private static HashSet<string> GetChangedDriveRoots(
    DriveSetSnapshot previous,
    DriveSetSnapshot current)
    {
        Dictionary<string, SharedDriveSnapshot> previousByRoot = previous.Drives
            .Where(static d => !string.IsNullOrWhiteSpace(d.DriveRoot))
            .ToDictionary(
                static d => d.DriveRoot,
                static d => d,
                StringComparer.OrdinalIgnoreCase);

        HashSet<string> changedRoots = new(StringComparer.OrdinalIgnoreCase);

        foreach (SharedDriveSnapshot currentDrive in current.Drives)
        {
            if (string.IsNullOrWhiteSpace(currentDrive.DriveRoot))
                continue;

            if (!previousByRoot.TryGetValue(currentDrive.DriveRoot, out SharedDriveSnapshot? previousDrive) ||
                previousDrive != currentDrive)
            {
                changedRoots.Add(currentDrive.DriveRoot);
            }

            previousByRoot.Remove(currentDrive.DriveRoot);
        }

        foreach (string removedRoot in previousByRoot.Keys)
            changedRoots.Add(removedRoot);

        return changedRoots;
    }

    private static void ApplySnapshotToWindows(
        IReadOnlyList<IExplorerWindow> windows,
        DriveSetSnapshot snapshot,
        RefreshReason reason)
    {
        foreach (IExplorerWindow window in windows)
            window.ApplyDriveSetSnapshot(snapshot, reason);
    }

    private static void ApplyDriveToWindows(
        IReadOnlyList<IExplorerWindow> windows,
        SharedDriveSnapshot snapshot,
        RefreshReason reason)
    {
        foreach (IExplorerWindow window in windows)
            window.ApplyDriveSnapshot(snapshot, reason);
    }

    private static void RequestCurrentViewRefresh(
        IReadOnlyList<IExplorerWindow> windows,
        RefreshReason reason)
    {
        foreach (IExplorerWindow window in windows)
            window.RequestRefreshCurrentView(reason);
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
}