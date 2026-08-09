using Explorer.UI.Icons;
using Shared.Shell.Interop;
using Shared.Shell.Models;
using Shell.Core.Interfaces;
using Shell.Core.Models;
using System.IO;
using SharedDriveSnapshot = Shared.Shell.Models.DriveSnapshot;

namespace Explorer.UI.Shell;

internal sealed class ExplorerShellWindowPresenter
{
    internal const string ThisPcPath = "::ThisPC::";

    private readonly ExplorerShellWindow _view;
    private readonly IExplorerShellCommands _commands;
    private readonly IExplorerDirectoryService _directoryService;
    private readonly IExplorerCommandService _commandService;
    private readonly ExplorerIconCache _iconCache;

    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();

    private string _currentPath;
    private DriveSetSnapshot _current = new()
    {
        Drives = [],
        RefreshedUtc = DateTime.MinValue
    };

    private string? _loadedDirectoryPath;
    private IReadOnlyList<ExplorerListRow> _loadedDirectoryRows = [];
    private string? _loadedDirectoryStatusText;
    private bool _usePreloadedDirectoryForInitialRefresh;
    private string? _pendingListRenamePath;

    private string? _loadingDirectoryPath;
    private CancellationTokenSource? _directoryLoadCts;
    private int _directoryLoadRequestId;
    private readonly string _windowId;
    private readonly ExplorerWindowMode _mode;
    private ExplorerIconKey? _windowIconKey;

    private readonly Dictionary<string, CancellationTokenSource> _treeChildLoadCtsByPath =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, int> _treeChildLoadRequestIdsByPath =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _treeChildLoadPathsWaitingForDirectoryLoad =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _treeInitialized;

    public ExplorerShellWindowPresenter(
        ExplorerShellWindow view,
        IExplorerShellCommands commands,
        IExplorerDirectoryService directoryService,
        IExplorerCommandService commandService,
        ExplorerIconCache iconCache,
        string windowId,
        ExplorerWindowOptions options)
    {
        _view = view;
        _commands = commands;
        _directoryService = directoryService;
        _commandService = commandService;
        _iconCache = iconCache;
        _windowId = windowId;
        _mode = options.Mode;
        _currentPath = NormalizeInitialPath(options.InitialPath) ?? ThisPcPath;

        ApplyPreloadedDirectoryListing(options.PreloadedDirectoryListing);
    }

    public ExplorerWindowMode Mode => _mode;

    public string? CurrentFileSystemPath => IsThisPcPath(_currentPath) ? null : _currentPath;

    public ExplorerWindowState CreateWindowState()
    {
        bool isThisPc = string.Equals(_currentPath, ThisPcPath, StringComparison.Ordinal);

        return new ExplorerWindowState
        {
            WindowId = _windowId,
            CurrentPath = isThisPc ? null : _currentPath,
            CurrentDriveRoot = isThisPc ? null : TryGetDriveRoot(_currentPath),
            IsThisPcView = isThisPc
        };
    }

    public void OnViewClosed()
    {
        CancelPendingDirectoryLoad();
        CancelAllPendingTreeChildLoads();
        InvalidateLoadedDirectoryCache();
        _windowIconKey = null;
        _loadingDirectoryPath = null;
        _backHistory.Clear();
        _forwardHistory.Clear();
    }

    public void OpenNewWindow()
    {
        string? initialPath = string.Equals(_currentPath, ThisPcPath, StringComparison.Ordinal)
            ? null
            : _currentPath;

        _commands.OpenNewWindow(
            initialPath,
            CreatePreloadedDirectoryListingForPath(initialPath));
    }

    private bool TryOpenNewWindowWithPreloadedDirectory(string? targetPath)
    {
        ExplorerPreloadedDirectoryListing? preloadedDirectoryListing =
            CreatePreloadedDirectoryListingForPath(targetPath);

        if (preloadedDirectoryListing == null)
            return false;

        _commands.OpenNewWindow(targetPath, preloadedDirectoryListing);
        return true;
    }

    private ExplorerPreloadedDirectoryListing? CreatePreloadedDirectoryListingForPath(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) ||
            !PathsEqualForNav(_currentPath, targetPath) ||
            !PathsEqualForNav(_loadedDirectoryPath, targetPath) ||
            !string.IsNullOrWhiteSpace(_loadedDirectoryStatusText))
        {
            return null;
        }

        ExplorerPreloadedDirectoryRow[] rows = new ExplorerPreloadedDirectoryRow[_loadedDirectoryRows.Count];
        int rowCount = 0;

        foreach (ExplorerListRow row in _loadedDirectoryRows)
        {
            if (row.Kind is not ExplorerListRowKind.Directory and not ExplorerListRowKind.File)
                continue;

            rows[rowCount++] = new ExplorerPreloadedDirectoryRow(
                isDirectory: row.Kind == ExplorerListRowKind.Directory,
                displayName: row.DisplayName,
                fullPath: row.FullPath,
                typeText: row.TypeText,
                extension: row.Extension,
                isVisibleHidden: row.IsVisibleHidden,
                modifiedLocalTime: row.ModifiedLocalTime,
                sizeBytes: row.SizeBytes);
        }

        if (rowCount != rows.Length)
            Array.Resize(ref rows, rowCount);

        return new ExplorerPreloadedDirectoryListing
        {
            DirectoryPath = targetPath,
            Rows = rows
        };
    }

    private void ApplyPreloadedDirectoryListing(ExplorerPreloadedDirectoryListing? listing)
    {
        if (listing == null ||
            string.IsNullOrWhiteSpace(listing.DirectoryPath) ||
            IsThisPcPath(_currentPath))
        {
            return;
        }

        string? normalizedListingPath = NormalizeInitialPath(listing.DirectoryPath);
        if (string.IsNullOrWhiteSpace(normalizedListingPath) ||
            !PathsEqualForNav(normalizedListingPath, _currentPath))
        {
            return;
        }

        _loadedDirectoryPath = _currentPath;
        _loadedDirectoryRows = MapPreloadedDirectoryRows(listing.Rows);
        _loadedDirectoryStatusText = null;
        _usePreloadedDirectoryForInitialRefresh = true;
    }

    public void ApplyDriveSetSnapshot(DriveSetSnapshot snapshot, RefreshReason reason)
    {
        _current = snapshot;
        ApplyUpdatedDriveSetModel(reason);
    }

    public void ApplyDriveSnapshot(SharedDriveSnapshot snapshot, RefreshReason reason)
    {
        List<SharedDriveSnapshot> updated = _current.Drives.ToList();
        int index = updated.FindIndex(d => string.Equals(d.DriveRoot, snapshot.DriveRoot, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
            updated[index] = snapshot;
        else
            updated.Add(snapshot);

        _current = new DriveSetSnapshot
        {
            Drives = updated,
            RefreshedUtc = DateTime.UtcNow
        };

        ApplyUpdatedDriveSnapshot(snapshot, reason);
    }

    public void RequestRefreshCurrentView(RefreshReason reason)
    {
        bool usePreloadedDirectory =
            _usePreloadedDirectoryForInitialRefresh &&
            reason == RefreshReason.InternalRequest &&
            !IsThisPcPath(_currentPath) &&
            PathsEqualForNav(_loadedDirectoryPath, _currentPath);

        _usePreloadedDirectoryForInitialRefresh = false;

        Render(
            forceDirectoryReload: !usePreloadedDirectory && !IsThisPcPath(_currentPath),
            refreshTreeChrome: reason != RefreshReason.ManualRefresh);
    }

    public void ReloadTreeDriveRoots()
    {
        RefreshTreeChrome(forceDriveRootReload: true);
    }

    public void PrepareTreeForDpiChange()
    {
        CancelAllPendingTreeChildLoads();
        _treeInitialized = false;
        _view.ReleaseTreeResourcesForDpiChange();
    }

    public void RefreshLoadedTreeFolderChildren(string parentPath)
    {
        if (string.IsNullOrWhiteSpace(parentPath) || IsThisPcPath(parentPath))
            return;

        if (!_treeInitialized || !_view.PrepareTreeFolderChildrenRefresh(parentPath))
            return;

        StartTreeChildLoad(parentPath, showLoading: false);
    }

    public void LoadTreeFolderChildrenForExpansion(string parentPath)
    {
        if (string.IsNullOrWhiteSpace(parentPath) || IsThisPcPath(parentPath))
            return;

        if (!_treeInitialized)
            return;

        StartTreeChildLoad(parentPath, showLoading: true);
    }

    public void RetargetCurrentPath(string oldPath, string newPath)
    {
        string rewrittenCurrentPath = RewriteRenamedPath(_currentPath, oldPath, newPath);
        bool currentPathChanged = !PathsEqualForNav(_currentPath, rewrittenCurrentPath);

        UpdateNavigationHistoryAfterRename(oldPath, newPath);

        if (!currentPathChanged)
            return;

        CancelPendingDirectoryLoad();
        InvalidateLoadedDirectoryCache();
        _currentPath = rewrittenCurrentPath;
    }

    public void RetargetDeletedPath(string deletedPath, string fallbackPath)
    {
        string rewrittenCurrentPath = RewriteDeletedPath(_currentPath, deletedPath, fallbackPath);
        bool currentPathChanged = !PathsEqualForNav(_currentPath, rewrittenCurrentPath);

        UpdateNavigationHistoryAfterDelete(deletedPath, fallbackPath);

        if (!currentPathChanged)
            return;

        CancelPendingDirectoryLoad();
        InvalidateLoadedDirectoryCache();
        _currentPath = rewrittenCurrentPath;
    }

    private void ApplyUpdatedDriveSetModel(RefreshReason reason)
    {
        bool switchedToThisPc = RetargetToThisPcIfCurrentPathIsUnavailable();

        ReloadTreeDriveRoots();

        if (switchedToThisPc || IsThisPcPath(_currentPath))
            RequestRefreshCurrentView(reason);
    }

    private void ApplyUpdatedDriveSnapshot(SharedDriveSnapshot snapshot, RefreshReason reason)
    {
        bool switchedToThisPc = RetargetToThisPcIfCurrentPathIsUnavailable();

        if (switchedToThisPc)
        {
            ReloadTreeDriveRoots();
            RequestRefreshCurrentView(reason);
            return;
        }

        _view.UpdateTreeDrive(snapshot, _currentPath);

        string? currentDriveRoot = TryGetDriveRoot(_currentPath);
        if (!string.IsNullOrWhiteSpace(currentDriveRoot) &&
            string.Equals(currentDriveRoot, snapshot.DriveRoot, StringComparison.OrdinalIgnoreCase))
        {
            UpdateNavigationChrome();
        }

        if (IsThisPcPath(_currentPath))
            RequestRefreshCurrentView(reason);
    }

    private bool RetargetToThisPcIfCurrentPathIsUnavailable()
    {
        if (IsThisPcPath(_currentPath) || IsPathStillAvailable(_currentPath))
            return false;

        CancelPendingDirectoryLoad();
        InvalidateLoadedDirectoryCache();
        _currentPath = ThisPcPath;
        return true;
    }

    public void NavigateBack()
    {
        if (_backHistory.Count == 0)
            return;

        _forwardHistory.Push(_currentPath);
        CancelPendingDirectoryLoad();
        InvalidateLoadedDirectoryCache();
        _currentPath = _backHistory.Pop();
        Render(forceDirectoryReload: !IsThisPcPath(_currentPath));
    }

    public void NavigateForward()
    {
        if (_forwardHistory.Count == 0)
            return;

        _backHistory.Push(_currentPath);
        CancelPendingDirectoryLoad();
        InvalidateLoadedDirectoryCache();
        _currentPath = _forwardHistory.Pop();
        Render(forceDirectoryReload: !IsThisPcPath(_currentPath));
    }

    public void NavigateUp()
    {
        if (IsThisPcPath(_currentPath))
            return;

        string? parent = TryGetParentLocation(_currentPath);
        if (parent == null)
            return;

        NavigateTo(parent, addToHistory: true);
    }

    public void NavigateFromAddressBar(string rawText)
    {
        string? normalized = NormalizeAddressInput(rawText);
        if (normalized == null)
            return;

        NavigateTo(normalized, addToHistory: true);
    }

    public void NavigateToPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        NavigateTo(path, addToHistory: true);
    }

    public void HandleTreeNodeActivate(ExplorerTreeNodeTag? tag)
    {
        if (tag == null)
            return;

        ActivateTarget(
            MapTreeNodeTargetKind(tag.Kind),
            tag.Path,
            tag.DriveType,
            tag.IsReady,
            tag.IsLocked,
            tag.IssueKind,
            tag.IssueMessage);
    }

    public void HandleListItemActivate(ExplorerListRow? row)
    {
        if (row == null)
            return;

        ActivateTarget(
            MapListRowTargetKind(row.Kind),
            row.FullPath,
            row.DriveType,
            row.IsReady,
            row.IsLocked,
            row.IssueKind,
            row.IssueMessage);
    }

    public ExplorerCommandContext CreateListItemCommandContext(
        ExplorerListRow row,
        IReadOnlyList<string> selectionPaths)
    {
        return new ExplorerCommandContext
        {
            WindowId = _windowId,
            TargetKind = MapListRowTargetKind(row.Kind),
            TargetPath = row.FullPath,
            SelectionPaths = NormalizeTransferPaths(selectionPaths),
            CurrentLocation = IsThisPcPath(_currentPath) ? null : _currentPath,
            IsThisPcView = IsThisPcPath(_currentPath),
            IsBackground = false,
            IsTreeTarget = false,
            DriveType = row.DriveType,
            IsReady = row.IsReady,
            IsLocked = row.IsLocked,
            IsBitLockerProtected = row.IsBitLockerProtected,
            IssueKind = row.IssueKind,
            IssueHResult = row.IssueHResult,
            IssueMessage = row.IssueMessage,
            CanUseExplorerBitLockerUi = _commands.CanUseExplorerBitLockerUi,
            CanEjectDriveDevice = CanEjectDriveDevice(row.DriveType, row.FullPath),
            CanPaste = CanPasteIntoPath(row.FullPath),
            CanCreateFolder = false,
            CanShowCurrentLocationProperties = false,
            IsNotepadAvailable = IsNotepadAvailable()
        };
    }

    public ExplorerCommandContext CreateBackgroundCommandContext()
    {
        bool isThisPc = IsThisPcPath(_currentPath);

        return new ExplorerCommandContext
        {
            WindowId = _windowId,
            TargetKind = isThisPc
                ? ExplorerCommandTargetKind.BackgroundThisPc
                : ExplorerCommandTargetKind.BackgroundFolder,
            TargetPath = null,
            SelectionPaths = [],
            CurrentLocation = isThisPc ? null : _currentPath,
            IsThisPcView = isThisPc,
            IsBackground = true,
            IsTreeTarget = false,
            CanUseExplorerBitLockerUi = _commands.CanUseExplorerBitLockerUi,
            CanPaste = CanPasteIntoCurrentLocation(),
            CanCreateFolder = !isThisPc && CanCreateNewFolderInCurrentLocation(),
            CanShowCurrentLocationProperties = !isThisPc && Directory.Exists(_currentPath),
            IsNotepadAvailable = IsNotepadAvailable()
        };
    }

    public ExplorerCommandContext CreateTreeNodeCommandContext(ExplorerTreeNodeTag tag)
    {
        bool isThisPcNode = tag.Kind == ExplorerTreeNodeKind.ThisPc;

        return new ExplorerCommandContext
        {
            WindowId = _windowId,
            TargetKind = MapTreeNodeTargetKind(tag.Kind),
            TargetPath = isThisPcNode ? null : tag.Path,
            SelectionPaths = [],
            CurrentLocation = IsThisPcPath(_currentPath) ? null : _currentPath,
            IsThisPcView = IsThisPcPath(_currentPath),
            IsBackground = false,
            IsTreeTarget = true,
            DriveType = tag.DriveType,
            IsReady = tag.IsReady,
            IsLocked = tag.IsLocked,
            IsBitLockerProtected = tag.IsBitLockerProtected,
            IssueKind = tag.IssueKind,
            IssueHResult = tag.IssueHResult,
            IssueMessage = tag.IssueMessage,
            CanUseExplorerBitLockerUi = _commands.CanUseExplorerBitLockerUi,
            CanEjectDriveDevice = CanEjectDriveDevice(tag.DriveType, tag.Path),
            CanPaste = CanPasteIntoPath(tag.Path),
            CanCreateFolder = false,
            CanShowCurrentLocationProperties = false,
            IsNotepadAvailable = IsNotepadAvailable()
        };
    }

    public ExplorerCommandContext CreateSelectionCommandContext(IReadOnlyList<string> selectionPaths)
    {
        return new ExplorerCommandContext
        {
            WindowId = _windowId,
            TargetKind = ExplorerCommandTargetKind.None,
            TargetPath = null,
            SelectionPaths = NormalizeTransferPaths(selectionPaths),
            CurrentLocation = IsThisPcPath(_currentPath) ? null : _currentPath,
            IsThisPcView = IsThisPcPath(_currentPath),
            IsBackground = false,
            IsTreeTarget = false,
            CanUseExplorerBitLockerUi = _commands.CanUseExplorerBitLockerUi,
            CanPaste = false,
            CanCreateFolder = false,
            CanShowCurrentLocationProperties = false,
            IsNotepadAvailable = IsNotepadAvailable()
        };
    }

    private bool CanEjectDriveDevice(DriveType? driveType, string? path)
    {
        if (driveType is DriveType.Network or DriveType.CDRom || string.IsNullOrWhiteSpace(path))
            return false;

        return _commands.CanEjectDriveDevice(path);
    }

    public IReadOnlyList<ExplorerMenuItemModel> BuildContextMenu(ExplorerCommandContext context)
    {
        return _commandService.BuildContextMenu(context);
    }

    public bool ClearClipboard()
    {
        return _commands.ClearClipboard();
    }

    public bool ExecuteContextCommand(
        ExplorerCommandId commandId,
        string? commandArgument,
        ExplorerCommandContext context,
        Action beginInlineRename)
    {
        if (commandId == ExplorerCommandId.Rename)
        {
            beginInlineRename();
            return true;
        }

        if (commandId == ExplorerCommandId.Open)
        {
            return ActivateTarget(
                context.TargetKind,
                context.TargetPath,
                context.DriveType,
                context.IsReady,
                context.IsLocked,
                context.IssueKind,
                context.IssueMessage);
        }

        if (commandId == ExplorerCommandId.OpenInNewWindow &&
            TryOpenNewWindowWithPreloadedDirectory(context.TargetPath))
        {
            return true;
        }

        if (commandId == ExplorerCommandId.NewFolder)
            return CreateNewFolderAndBeginRename(context);

        return _commandService.TryExecute(commandId, context, commandArgument);
    }

    public bool ExecuteContextCommand(
        ExplorerCommandId commandId,
        ExplorerCommandContext context,
        Action beginInlineRename)
    {
        return ExecuteContextCommand(commandId, null, context, beginInlineRename);
    }

    public bool CommitListRename(ExplorerListRow row, string editedText)
    {
        return row.Kind switch
        {
            ExplorerListRowKind.File => CommitFileSystemRename(row.FullPath, isDirectory: false, editedText),
            ExplorerListRowKind.Directory => CommitFileSystemRename(row.FullPath, isDirectory: true, editedText),
            ExplorerListRowKind.Drive => CommitDriveRename(row.FullPath, editedText),
            _ => false
        };
    }

    public bool CommitTreeRename(ExplorerTreeNodeTag tag, string editedText)
    {
        if (string.IsNullOrWhiteSpace(tag.Path))
            return false;

        return tag.Kind switch
        {
            ExplorerTreeNodeKind.Drive => CommitDriveRename(tag.Path, editedText),
            ExplorerTreeNodeKind.Folder => CommitFileSystemRename(tag.Path, isDirectory: true, editedText),
            _ => false
        };
    }

    private static bool HasDriveIssue(DriveIssueKind? issueKind)
    {
        return issueKind.HasValue && issueKind.Value != DriveIssueKind.None;
    }

    private void RestoreTreeSelectionToThisPc()
    {
        if (_view.IsDisposed)
            return;

        void Restore()
        {
            if (!_view.IsDisposed)
                _view.RestoreBestExistingTreeSelectionForPath(ThisPcPath);
        }

        if (!_view.IsHandleCreated)
        {
            Restore();
            return;
        }

        try
        {
            _view.BeginInvoke(new Action(Restore));
        }
        catch (InvalidOperationException)
        {
            Restore();
        }
    }

    private bool ActivateTarget(
        ExplorerCommandTargetKind targetKind,
        string? path,
        DriveType? driveType = null,
        bool? isReady = null,
        bool? isLocked = null,
        DriveIssueKind? issueKind = null,
        string? issueMessage = null)
    {
        switch (targetKind)
        {
            case ExplorerCommandTargetKind.ThisPc:
                NavigateTo(ThisPcPath, addToHistory: true);
                return true;

            case ExplorerCommandTargetKind.Drive:
                if (string.IsNullOrWhiteSpace(path))
                    return false;

                if (driveType == DriveType.CDRom && issueKind == DriveIssueKind.OpticalNoMedia)
                {
                    _commands.ShowOpticalDriveEmptyMessage(path);
                    RestoreTreeSelectionToThisPc();

                    return true;
                }

                if (isLocked == true)
                {
                    if (_commands.CanUseExplorerBitLockerUi)
                        return LaunchBitLockerUnlockForNavigation(path, openInNewWindowAfterUnlock: false);

                    _commands.ShowDriveNotReadyMessage(path, issueKind, issueMessage);
                    RestoreTreeSelectionToThisPc();

                    return true;
                }

                if (HasDriveIssue(issueKind))
                {
                    _commands.ShowDriveNotReadyMessage(path, issueKind, issueMessage);
                    RestoreTreeSelectionToThisPc();

                    return true;
                }

                if (isReady == false)
                {
                    _commands.ShowDriveNotReadyMessage(path, issueKind, issueMessage);
                    RestoreTreeSelectionToThisPc();

                    return true;
                }

                return NavigateFromContextTarget(path);

            case ExplorerCommandTargetKind.Folder:
                if (string.IsNullOrWhiteSpace(path))
                    return false;

                return NavigateFromContextTarget(path);

            case ExplorerCommandTargetKind.File:
                if (string.IsNullOrWhiteSpace(path))
                    return false;

                return OpenFileFromContextTarget(path);

            default:
                return false;
        }
    }

    private static ExplorerCommandTargetKind MapListRowTargetKind(ExplorerListRowKind kind)
    {
        return kind switch
        {
            ExplorerListRowKind.File => ExplorerCommandTargetKind.File,
            ExplorerListRowKind.Directory => ExplorerCommandTargetKind.Folder,
            ExplorerListRowKind.Drive => ExplorerCommandTargetKind.Drive,
            _ => ExplorerCommandTargetKind.None
        };
    }

    private static ExplorerCommandTargetKind MapTreeNodeTargetKind(ExplorerTreeNodeKind kind)
    {
        return kind switch
        {
            ExplorerTreeNodeKind.ThisPc => ExplorerCommandTargetKind.ThisPc,
            ExplorerTreeNodeKind.Drive => ExplorerCommandTargetKind.Drive,
            ExplorerTreeNodeKind.Folder => ExplorerCommandTargetKind.Folder,
            _ => ExplorerCommandTargetKind.None
        };
    }

    private bool CanCreateNewFolderInCurrentLocation()
    {
        return !IsThisPcPath(_currentPath) &&
               Directory.Exists(_currentPath);
    }

    private bool CanPasteIntoCurrentLocation()
    {
        return CanPasteIntoPath(_currentPath);
    }

    private bool CanPasteIntoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsThisPcPath(path) || !Directory.Exists(path))
            return false;

        return _commands.CanPasteFileTransfer();
    }

    private bool NavigateFromContextTarget(string path)
    {
        string normalizedPath = NormalizeInitialPath(path) ?? path;

        if (!Directory.Exists(normalizedPath))
            return false;

        NavigateTo(normalizedPath, addToHistory: true);
        return true;
    }

    private bool LaunchBitLockerUnlockForNavigation(string path, bool openInNewWindowAfterUnlock)
    {
        string? driveRoot = TryGetDriveRoot(path) ?? NormalizeInitialPath(path);
        if (string.IsNullOrWhiteSpace(driveRoot))
            return false;

        _commands.LaunchBitLockerHelper(
            driveRoot,
            ExplorerBitLockerAction.Unlock,
            driveRoot,
            _windowId,
            openInNewWindowAfterUnlock);

        return true;
    }

    private bool OpenFileFromContextTarget(string path)
    {
        string normalizedPath = NormalizeInitialPath(path) ?? path;

        if (!File.Exists(normalizedPath))
            return false;

        _commands.OpenFileSystemItem(normalizedPath);
        return true;
    }

    private static bool IsNotepadAvailable()
    {
        string systemDirectory = Environment.SystemDirectory;
        if (string.IsNullOrWhiteSpace(systemDirectory))
            return false;

        return File.Exists(Path.Combine(systemDirectory, "notepad.exe"));
    }

    private static string[] NormalizeTransferPaths(IReadOnlyList<string> paths)
    {
        return (paths ?? [])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Where(static path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void NavigateTo(string targetPath, bool addToHistory)
    {
        string normalizedTarget = NormalizeInitialPath(targetPath) ?? targetPath;

        if (PathsEqualForNav(_currentPath, normalizedTarget))
            return;

        if (addToHistory)
            _backHistory.Push(_currentPath);

        _forwardHistory.Clear();
        CancelPendingDirectoryLoad();
        InvalidateLoadedDirectoryCache();
        _currentPath = normalizedTarget;

        Render(forceDirectoryReload: !IsThisPcPath(_currentPath));
    }

    private void Render(
        bool forceDirectoryReload = false,
        bool refreshTreeChrome = true)
    {
        if (refreshTreeChrome)
            RefreshTreeChrome(forceDriveRootReload: false);
        else
            UpdateNavigationChrome();

        if (IsThisPcPath(_currentPath))
        {
            CancelPendingDirectoryLoad();
            InvalidateLoadedDirectoryCache();

            IReadOnlyList<ExplorerListRow> rows = BuildDriveRows();
            _view.ShowDriveRows(rows);
            _view.UpdateBrowseStatusTextFromSelection();
            return;
        }

        if (!forceDirectoryReload && PathsEqualForNav(_loadedDirectoryPath, _currentPath))
        {
            _view.ShowDirectoryRows(_loadedDirectoryRows);
            if (string.IsNullOrWhiteSpace(_loadedDirectoryStatusText))
            {
                _view.UpdateBrowseStatusTextFromSelection();
                _view.UpdateTreePathChildHintFromListRows(_currentPath, _loadedDirectoryRows);
            }
            else
            {
                _view.SetStatusText(_loadedDirectoryStatusText);
            }

            return;
        }

        if (!forceDirectoryReload && PathsEqualForNav(_loadingDirectoryPath, _currentPath))
        {
            _view.SetStatusText("Loading...");
            return;
        }

        bool keepExistingRowsWhileLoading =
            forceDirectoryReload &&
            PathsEqualForNav(_loadedDirectoryPath, _currentPath) &&
            string.IsNullOrWhiteSpace(_loadedDirectoryStatusText);

        StartDirectoryLoad(
            _currentPath,
            clearRowsBeforeLoad: !keepExistingRowsWhileLoading);
    }

    private void RefreshTreeChrome(bool forceDriveRootReload)
    {
        if (forceDriveRootReload || !_treeInitialized)
        {
            _view.ShowTree(_current.Drives, _currentPath);
            _treeInitialized = true;
        }

        _view.SelectBestExistingTreeNodeForPath(_currentPath);

        UpdateNavigationChrome();
    }

    private void StartTreeChildLoad(string parentPath, bool showLoading)
    {
        CancelPendingTreeChildLoad(parentPath);

        if (showLoading && PathsEqualForNav(_loadingDirectoryPath, parentPath))
        {
            _treeChildLoadPathsWaitingForDirectoryLoad.Add(parentPath);
            _view.ShowTreeFolderLoading(parentPath);
            return;
        }

        if (showLoading && TryShowTreeFolderChildrenFromLoadedDirectory(parentPath))
            return;

        CancellationTokenSource cts = new();
        int requestId = NextTreeChildLoadRequestId(parentPath);

        _treeChildLoadCtsByPath[parentPath] = cts;

        if (showLoading)
            _view.ShowTreeFolderLoading(parentPath);

        LoadTreeFolderChildrenAsync(parentPath, requestId, cts);
    }

    private async void LoadTreeFolderChildrenAsync(string parentPath, int requestId, CancellationTokenSource cts)
    {
        IReadOnlyList<ExplorerDirectoryItem> childDirectories;
        CancellationToken cancellationToken = cts.Token;

        try
        {
            childDirectories = await _directoryService.LoadChildDirectoriesAsync(parentPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            childDirectories = [];
        }
        finally
        {
            cts.Dispose();
        }

        if (_view.IsDisposed || cancellationToken.IsCancellationRequested)
            return;

        if (!_treeChildLoadRequestIdsByPath.TryGetValue(parentPath, out int currentRequestId) ||
            currentRequestId != requestId)
        {
            return;
        }

        _treeChildLoadCtsByPath.Remove(parentPath);
        _treeChildLoadRequestIdsByPath.Remove(parentPath);

        _view.ShowTreeFolderChildren(parentPath, childDirectories);
    }

    private bool TryShowTreeFolderChildrenFromLoadedDirectory(string parentPath)
    {
        if (!PathsEqualForNav(_loadedDirectoryPath, parentPath) ||
            !string.IsNullOrWhiteSpace(_loadedDirectoryStatusText))
        {
            return false;
        }

        _view.ShowTreeFolderChildrenFromListRows(parentPath, _loadedDirectoryRows);
        return true;
    }

    private void CompleteTreeChildLoadsWaitingForDirectoryLoad(
        string directoryPath,
        IReadOnlyList<ExplorerListRow> rows)
    {
        if (_treeChildLoadPathsWaitingForDirectoryLoad.Count == 0)
            return;

        string[] waitingPaths = _treeChildLoadPathsWaitingForDirectoryLoad
            .Where(path => PathsEqualForNav(path, directoryPath))
            .ToArray();

        foreach (string parentPath in waitingPaths)
        {
            _treeChildLoadPathsWaitingForDirectoryLoad.Remove(parentPath);
            _view.ShowTreeFolderChildrenFromListRows(parentPath, rows);
        }
    }

    private void CancelTreeChildLoadsWaitingForDirectoryLoad(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) ||
            _treeChildLoadPathsWaitingForDirectoryLoad.Count == 0)
        {
            return;
        }

        string[] waitingPaths = _treeChildLoadPathsWaitingForDirectoryLoad
            .Where(path => PathsEqualForNav(path, directoryPath))
            .ToArray();

        foreach (string parentPath in waitingPaths)
        {
            _treeChildLoadPathsWaitingForDirectoryLoad.Remove(parentPath);
            _view.CancelTreeFolderLoading(parentPath);
        }
    }

    private int NextTreeChildLoadRequestId(string parentPath)
    {
        int nextRequestId = 1;

        if (_treeChildLoadRequestIdsByPath.TryGetValue(parentPath, out int currentRequestId))
            nextRequestId = currentRequestId + 1;

        _treeChildLoadRequestIdsByPath[parentPath] = nextRequestId;
        return nextRequestId;
    }

    private void CancelPendingTreeChildLoad(string parentPath)
    {
        bool hadPendingLoad = _treeChildLoadCtsByPath.TryGetValue(
            parentPath,
            out CancellationTokenSource? cts);

        if (hadPendingLoad)
        {
            try
            {
                cts!.Cancel();
            }
            catch
            {
            }

            _treeChildLoadCtsByPath.Remove(parentPath);
            _view.CancelTreeFolderLoading(parentPath);
        }

        if (_treeChildLoadPathsWaitingForDirectoryLoad.Remove(parentPath))
            _view.CancelTreeFolderLoading(parentPath);

        _treeChildLoadRequestIdsByPath.Remove(parentPath);
    }

    private void CancelAllPendingTreeChildLoads()
    {
        string[] pendingPaths = _treeChildLoadCtsByPath.Keys.ToArray();

        foreach (string parentPath in pendingPaths)
        {
            if (!_treeChildLoadCtsByPath.TryGetValue(parentPath, out CancellationTokenSource? cts))
                continue;

            try
            {
                cts.Cancel();
            }
            catch
            {
            }

            _view.CancelTreeFolderLoading(parentPath);
        }

        foreach (string parentPath in _treeChildLoadPathsWaitingForDirectoryLoad.ToArray())
            _view.CancelTreeFolderLoading(parentPath);

        _treeChildLoadCtsByPath.Clear();
        _treeChildLoadRequestIdsByPath.Clear();
        _treeChildLoadPathsWaitingForDirectoryLoad.Clear();
    }

    private void StartDirectoryLoad(string path, bool clearRowsBeforeLoad = true)
    {
        CancelPendingDirectoryLoad();

        if (clearRowsBeforeLoad)
            InvalidateLoadedDirectoryCache();

        CancellationTokenSource cts = new();
        int requestId = ++_directoryLoadRequestId;

        _directoryLoadCts = cts;
        _loadingDirectoryPath = path;

        if (clearRowsBeforeLoad)
        {
            _view.ShowDirectoryRows([]);
            _view.SetStatusText("Loading...");
        }
        else
        {
            _view.SetStatusText("Refreshing...");
        }

        LoadDirectoryAsync(path, requestId, cts);
    }

    private async void LoadDirectoryAsync(string path, int requestId, CancellationTokenSource cts)
    {
        IReadOnlyList<ExplorerListRow> rows = [];
        string? statusText = null;
        CancellationToken cancellationToken = cts.Token;

        try
        {
            ExplorerDirectoryListing listing = await _directoryService.LoadDirectoryAsync(path, cancellationToken);
            rows = MapDirectoryRows(listing.Items);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            statusText = ex.Message;
        }
        finally
        {
            cts.Dispose();
        }

        if (_view.IsDisposed || cancellationToken.IsCancellationRequested)
            return;

        if (requestId != _directoryLoadRequestId)
            return;

        if (!PathsEqualForNav(_currentPath, path))
            return;

        _directoryLoadCts = null;
        _loadingDirectoryPath = null;
        _loadedDirectoryPath = path;
        _loadedDirectoryRows = rows;
        _loadedDirectoryStatusText = statusText;

        _view.ShowDirectoryRows(rows);
        TryBeginPendingListRename(path);

        if (string.IsNullOrWhiteSpace(statusText))
        {
            _view.UpdateBrowseStatusTextFromSelection();
            _view.UpdateTreePathChildHintFromListRows(path, rows);
        }
        else
        {
            _view.SetStatusText(statusText);
        }

        CompleteTreeChildLoadsWaitingForDirectoryLoad(path, rows);
    }

    private void CancelPendingDirectoryLoad()
    {
        if (_directoryLoadCts == null)
            return;

        CancelTreeChildLoadsWaitingForDirectoryLoad(_loadingDirectoryPath);

        try
        {
            _directoryLoadCts.Cancel();
        }
        catch
        {
        }

        _directoryLoadCts = null;
        _loadingDirectoryPath = null;
    }

    private void InvalidateLoadedDirectoryCache()
    {
        _loadedDirectoryPath = null;
        _loadedDirectoryRows = [];
        _loadedDirectoryStatusText = null;
    }

    private List<ExplorerListRow> BuildDriveRows()
    {
        return _current.Drives
            .OrderBy(d => d.DriveRoot, StringComparer.OrdinalIgnoreCase)
            .Select(d => new ExplorerListRow
            {
                Kind = ExplorerListRowKind.Drive,
                DisplayName = d.DisplayName,
                FullPath = d.DriveRoot,
                DriveType = d.DriveType,
                IsReady = d.IsReady,
                IsLocked = d.IsEffectivelyBitLockerLocked,
                IsBitLockerProtected = d.IsBitLockerProtected,
                IssueKind = d.IssueKind,
                IssueHResult = d.IssueHResult,
                IssueMessage = d.IssueMessage,
                FreeSpaceBytes = d.FreeSpaceBytes,
                TotalSizeBytes = d.TotalSizeBytes,
                DriveVisualKind = d.VisualKind
            })
            .ToList();
    }

    private static List<ExplorerListRow> MapDirectoryRows(IReadOnlyList<ExplorerDirectoryItem> items)
    {
        List<ExplorerListRow> rows = new(items.Count);

        foreach (ExplorerDirectoryItem item in items)
        {
            rows.Add(new ExplorerListRow
            {
                Kind = item.IsDirectory ? ExplorerListRowKind.Directory : ExplorerListRowKind.File,
                DisplayName = item.Name,
                FullPath = item.FullPath,
                TypeText = item.TypeText,
                Extension = item.Extension,
                IsVisibleHidden = item.IsVisibleHidden,
                ModifiedLocalTime = item.ModifiedLocalTime,
                SizeBytes = item.SizeBytes
            });
        }

        return rows;
    }

    private static List<ExplorerListRow> MapPreloadedDirectoryRows(
    ExplorerPreloadedDirectoryRow[] rows)
    {
        List<ExplorerListRow> mappedRows = new(rows.Length);

        foreach (ExplorerPreloadedDirectoryRow row in rows)
        {
            mappedRows.Add(new ExplorerListRow
            {
                Kind = row.IsDirectory ? ExplorerListRowKind.Directory : ExplorerListRowKind.File,
                DisplayName = row.DisplayName,
                FullPath = row.FullPath,
                TypeText = row.TypeText,
                Extension = row.Extension,
                IsVisibleHidden = row.IsVisibleHidden,
                ModifiedLocalTime = row.ModifiedLocalTime,
                SizeBytes = row.SizeBytes
            });
        }

        return mappedRows;
    }

    private void UpdateNavigationChrome()
    {
        _view.SetAddressText(BuildAddressText(_currentPath));
        _view.SetWindowTitle(BuildWindowTitle(_currentPath));
        _view.RefreshCurrentLocationWindowIcon();
        _view.SetNavigationButtonState(
            canBack: _backHistory.Count > 0,
            canForward: _forwardHistory.Count > 0,
            canUp: CanNavigateUp(_currentPath));
    }

    internal Icon? CreateCurrentLocationWindowIcon(int size)
    {
        ExplorerIconKey key = GetCurrentLocationWindowIconKey(_currentPath, size);

        if (_windowIconKey.HasValue && _windowIconKey.Value.Equals(key))
            return null;

        Image image = _iconCache.GetImage(key);
        Icon? icon = CreateWindowIconFromImage(image, size);
        if (icon == null)
            return null;

        _windowIconKey = key;
        return icon;
    }

    private ExplorerIconKey GetCurrentLocationWindowIconKey(string path, int size)
    {
        if (IsThisPcPath(path))
            return ExplorerIconPolicy.GetThisPcIconKey(size);

        string normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? driveRoot = TryGetDriveRoot(path);

        if (!string.IsNullOrWhiteSpace(driveRoot))
        {
            string normalizedRoot = driveRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalized, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                SharedDriveSnapshot? drive = _current.Drives.FirstOrDefault(d =>
                    string.Equals(d.DriveRoot, driveRoot, StringComparison.OrdinalIgnoreCase));

                return ExplorerIconPolicy.GetDriveIconKey(drive?.VisualKind ?? DriveVisualKind.Fixed, size);
            }
        }

        return ExplorerIconPolicy.GetFolderIconKey(size, IsHiddenDirectory(path));
    }


    private static bool IsHiddenDirectory(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static Icon? CreateWindowIconFromImage(Image image, int size)
    {
        if (image == null)
            return null;

        IntPtr hIcon = IntPtr.Zero;

        try
        {
            using Bitmap bitmap = new(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            bitmap.SetResolution(96f, 96f);

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.DrawImage(image, new Rectangle(0, 0, size, size));
            }

            hIcon = bitmap.GetHicon();
            using Icon temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero)
                User32.DestroyIcon(hIcon);
        }
    }

    private string BuildWindowTitle(string path)
    {
        if (IsThisPcPath(path))
            return "This PC";

        string normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string? driveRoot = TryGetDriveRoot(path);
        if (!string.IsNullOrWhiteSpace(driveRoot))
        {
            string normalizedRoot = driveRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalized, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                SharedDriveSnapshot? drive = _current.Drives.FirstOrDefault(d =>
                    string.Equals(d.DriveRoot, driveRoot, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(drive?.DisplayName))
                    return drive.DisplayName;

                return driveRoot;
            }
        }

        string? name = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private bool IsPathStillAvailable(string path)
    {
        string? root = TryGetDriveRoot(path);
        if (string.IsNullOrWhiteSpace(root))
            return false;

        return _current.Drives.Any(d => string.Equals(d.DriveRoot, root, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsThisPcPath(string? path)
    {
        return string.Equals(path, ThisPcPath, StringComparison.Ordinal);
    }

    private static bool CanNavigateUp(string currentPath)
    {
        return !IsThisPcPath(currentPath);
    }

    private static string BuildAddressText(string currentPath)
    {
        return IsThisPcPath(currentPath) ? "This PC" : currentPath;
    }

    private static string? NormalizeAddressInput(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        if (string.Equals(rawText.Trim(), "This PC", StringComparison.OrdinalIgnoreCase))
            return ThisPcPath;

        return NormalizeInitialPath(rawText.Trim()) ?? rawText.Trim();
    }

    private static string? NormalizeInitialPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (string.Equals(path, ThisPcPath, StringComparison.Ordinal))
            return ThisPcPath;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetParentLocation(string currentPath)
    {
        if (IsThisPcPath(currentPath))
            return null;

        try
        {
            string? root = Path.GetPathRoot(currentPath);
            if (string.IsNullOrWhiteSpace(root))
                return ThisPcPath;

            string trimmedCurrent = currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(trimmedCurrent, trimmedRoot, StringComparison.OrdinalIgnoreCase))
                return ThisPcPath;

            DirectoryInfo? parent = Directory.GetParent(currentPath);
            if (parent == null)
                return ThisPcPath;

            return parent.FullName;
        }
        catch
        {
            return ThisPcPath;
        }
    }

    private static bool PathsEqualForNav(string? left, string? right)
    {
        if (left == null || right == null)
            return left == right;

        return string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetDriveRoot(string path)
    {
        try
        {
            return Path.GetPathRoot(path);
        }
        catch
        {
            return null;
        }
    }

    private bool CommitFileSystemRename(string path, bool isDirectory, string editedText)
    {
        string currentName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(currentName))
            return false;

        string newName = (editedText ?? string.Empty).Trim();

        if (string.Equals(newName, currentName, StringComparison.Ordinal))
            return false;

        return _commands.RenameFileSystemEntry(path, isDirectory, newName);
    }

    private bool CommitDriveRename(string rootPath, string editedText)
    {
        string newLabel = (editedText ?? string.Empty).Trim();
        return _commands.RenameDriveLabel(rootPath, newLabel);
    }

    private void UpdateNavigationHistoryAfterRename(string oldPath, string newPath)
    {
        RewriteHistoryStack(_backHistory, oldPath, newPath);
        RewriteHistoryStack(_forwardHistory, oldPath, newPath);
    }

    private bool CreateNewFolderAndBeginRename(ExplorerCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(context.CurrentLocation))
            return false;

        string? newFolderPath = _commands.CreateNewFolder(context.CurrentLocation);
        if (string.IsNullOrWhiteSpace(newFolderPath))
            return false;

        _pendingListRenamePath = newFolderPath;
        return true;
    }

    private void TryBeginPendingListRename(string loadedDirectoryPath)
    {
        string? pendingPath = _pendingListRenamePath;
        if (string.IsNullOrWhiteSpace(pendingPath))
            return;

        string? pendingParentPath = Path.GetDirectoryName(
            pendingPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (!PathsEqualForNav(pendingParentPath, loadedDirectoryPath))
            return;

        _pendingListRenamePath = null;
        _view.SelectListItemByPathAndBeginRename(pendingPath);
    }

    private static void RewriteHistoryStack(Stack<string> stack, string oldPath, string newPath)
    {
        if (stack.Count == 0)
            return;

        string[] rewritten = stack
            .Reverse()
            .Select(path => RewriteRenamedPath(path, oldPath, newPath))
            .ToArray();

        stack.Clear();

        foreach (string path in rewritten)
            stack.Push(path);
    }

    private void UpdateNavigationHistoryAfterDelete(string deletedPath, string fallbackPath)
    {
        RewriteDeletedHistoryStack(_backHistory, deletedPath, fallbackPath);
        RewriteDeletedHistoryStack(_forwardHistory, deletedPath, fallbackPath);
    }

    private static void RewriteDeletedHistoryStack(Stack<string> stack, string deletedPath, string fallbackPath)
    {
        if (stack.Count == 0)
            return;

        string[] rewritten = stack
            .Reverse()
            .Select(path => RewriteDeletedPath(path, deletedPath, fallbackPath))
            .ToArray();

        stack.Clear();

        foreach (string path in rewritten)
            stack.Push(path);
    }

    private static string RewriteRenamedPath(string? path, string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(path) || IsThisPcPath(path))
            return path ?? ThisPcPath;

        string oldPrefix = oldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string newPrefix = newPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (PathsEqualForNav(path, oldPath))
            return newPath;

        if (path.StartsWith(oldPrefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(oldPrefix + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(newPrefix, path.AsSpan(oldPrefix.Length));
        }

        return path;
    }

    private static string RewriteDeletedPath(string? path, string deletedPath, string fallbackPath)
    {
        if (string.IsNullOrWhiteSpace(path) || IsThisPcPath(path))
            return path ?? ThisPcPath;

        string deletedPrefix = deletedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (PathsEqualForNav(path, deletedPath))
            return fallbackPath;

        if (path.StartsWith(deletedPrefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(deletedPrefix + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return fallbackPath;
        }

        return path;
    }
}