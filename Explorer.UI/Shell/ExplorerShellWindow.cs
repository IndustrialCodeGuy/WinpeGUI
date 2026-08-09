using Explorer.UI.Icons;
using Explorer.UI.Layout;
using Shared.Shell.Interop;
using Shell.Core.FileTypes;
using Shell.Core.Interfaces;
using Shell.Core.Models;
using SharedDriveSnapshot = Shared.Shell.Models.DriveSnapshot;

namespace Explorer.UI.Shell;

public partial class ExplorerShellWindow : Form, IExplorerPickerWindow
{
    private readonly string _windowId = Guid.NewGuid().ToString("N");
    private readonly ExplorerShellWindowPresenter _presenter;
    private readonly ExplorerWindowMode _mode;
    private readonly string[] _allowedExtensionsDisplay;
    private Icon? _windowIcon;
    private bool _windowResourcesReleased;
    private ExplorerLayoutMetrics _mDip = new();
    private ExplorerLayoutMetricsPx _mPx = null!;
    private int _appliedDpi;
    private bool _layoutMetricsApplyQueued;
    private bool _applyingLayoutMetrics;
    private int _maxNavPaneWidthDip;
    private int _lastSplitAvailablePaneWidth;
    private int _lastNavPaneWidth;
    private bool _draggingSplitMainSplitter;
    private int _splitMainSplitterDragOffset;
    private Size _lastNormalClientSize;
    private Rectangle? _minimizedDpiRestoreBounds;
    private TreeDpiPrepareHook? _treeDpiPrepareHook;
    private bool _treePreparedForDpiChange;
    private bool _dpiRedrawFreezeActive;
    private readonly List<IntPtr> _dpiRedrawFrozenHandles = [];

    private Font? _toolbarGlyphFont;
    private Font? _addressFont;
    private Font? _addressSeparatorFont;
    private Font? _chromeFont;
    private IntPtr _addressTextBoxHFont;

    private readonly ExplorerIconCache _iconCache;
    private readonly IExplorerFileAssociationService _fileAssociations;

    public ExplorerShellWindow(
    IExplorerShellCommands commands,
    IExplorerDirectoryService directoryService,
    IExplorerCommandService commandService,
    ExplorerIconCache iconCache,
    IExplorerFileAssociationService fileAssociations,
    ExplorerWindowOptions options)
    {
        options ??= new ExplorerWindowOptions();

        // Match the taskbar scaling model. Explorer chrome is manually scaled
        // through ExplorerLayoutMetrics, so do not let WinForms apply a separate
        // form/control autoscale pass on top of those metrics.
        AutoScaleMode = AutoScaleMode.None;
        AutoScaleDimensions = new SizeF(96f, 96f);

        // Match the taskbar startup path: create the top-level handle before
        // calculating DeviceDpi-based metrics so startup above 100% uses the
        // actual monitor DPI.
        _ = Handle;

        _iconCache = iconCache;
        _fileAssociations = fileAssociations;
        _mode = options.Mode;
        _allowedExtensionsDisplay = BuildAllowedExtensionDisplayList(options.AllowedExtensions);

        InitializeComponent();

        ShowInTaskbar = _mode == ExplorerWindowMode.Browse;
        InitializeExplorerChrome();
        ApplyInitialWindowPlacement(options.Placement);

        _presenter = new ExplorerShellWindowPresenter(
            this,
            commands,
            directoryService,
            commandService,
            iconCache,
            _windowId,
            options);

        UpdateCurrentLocationWindowIcon();

        if (!string.IsNullOrWhiteSpace(options.Title))
            Text = options.Title;

        WireEvents();
    }

    public string WindowId => _windowId;

    public string? SelectedPath { get; private set; }

    public ExplorerWindowState GetWindowState()
    {
        return _presenter.CreateWindowState();
    }

    public ExplorerWindowPlacement? GetWindowPlacement()
    {
        if (_mode != ExplorerWindowMode.Browse)
            return null;

        Rectangle bounds = WindowState == FormWindowState.Normal
            ? Bounds
            : WindowState == FormWindowState.Minimized && _minimizedDpiRestoreBounds.HasValue
                ? _minimizedDpiRestoreBounds.Value
                : RestoreBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        EnsureMaxNavPaneWidth();

        return new ExplorerWindowPlacement
        {
            Bounds = bounds,
            IsMaximized = WindowState == FormWindowState.Maximized,
            NavPaneWidthDip = Math.Max(GetMinimumNavPaneWidthDip(), _maxNavPaneWidthDip)
        };
    }

    public void ApplyDriveSetSnapshot(DriveSetSnapshot snapshot, RefreshReason reason)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            TryBeginInvoke(() => ApplyDriveSetSnapshot(snapshot, reason));
            return;
        }

        _presenter.ApplyDriveSetSnapshot(snapshot, reason);
    }

    public void ApplyDriveSnapshot(SharedDriveSnapshot snapshot, RefreshReason reason)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            TryBeginInvoke(() => ApplyDriveSnapshot(snapshot, reason));
            return;
        }

        _presenter.ApplyDriveSnapshot(snapshot, reason);
    }

    public void RequestRefreshCurrentView(RefreshReason reason)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            TryBeginInvoke(() => RequestRefreshCurrentView(reason));
            return;
        }

        _presenter.RequestRefreshCurrentView(reason);
    }

    public void NavigateToPath(string path)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            TryBeginInvoke(() => NavigateToPath(path));
            return;
        }

        _presenter.NavigateToPath(path);
    }

    public void ReloadTreeDriveRoots()
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            TryBeginInvoke(ReloadTreeDriveRoots);
            return;
        }

        _presenter.ReloadTreeDriveRoots();
    }

    public void RefreshLoadedTreeFolderChildren(string parentPath)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            TryBeginInvoke(() => RefreshLoadedTreeFolderChildren(parentPath));
            return;
        }

        _presenter.RefreshLoadedTreeFolderChildren(parentPath);
    }

    public void RetargetCurrentPath(string oldPath, string newPath)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            TryBeginInvoke(() => RetargetCurrentPath(oldPath, newPath));
            return;
        }

        _presenter.RetargetCurrentPath(oldPath, newPath);
    }

    public void RetargetDeletedPath(string deletedPath, string fallbackPath)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            TryBeginInvoke(() => RetargetDeletedPath(deletedPath, fallbackPath));
            return;
        }

        _presenter.RetargetDeletedPath(deletedPath, fallbackPath);
    }

    public void ActivateWindow()
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            TryBeginInvoke(ActivateWindow);
            return;
        }

        bool shouldRemainMaximized = WindowState == FormWindowState.Maximized;

        Show();

        if (shouldRemainMaximized)
        {
            User32.ShowWindow(Handle, User32.SW_MAXIMIZE);
        }
        else
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            User32.ShowWindow(Handle, User32.SW_RESTORE);
        }

        BringToFront();
        Activate();
        User32.SetForegroundWindow(Handle);
    }

    private void WireEvents()
    {
        _btnBack.Click += (_, _) =>
        {
            if (IsToolbarGlyphButtonEnabled(_btnBack))
                _presenter.NavigateBack();
        };

        _btnForward.Click += (_, _) =>
        {
            if (IsToolbarGlyphButtonEnabled(_btnForward))
                _presenter.NavigateForward();
        };

        _btnUp.Click += (_, _) =>
        {
            if (IsToolbarGlyphButtonEnabled(_btnUp))
                _presenter.NavigateUp();
        };

        _btnRefresh.Click += (_, _) => ExecuteRefreshCurrentLocationCommand();
        _btnOk.Click += (_, _) => ExecutePickerOkCommand();
        _btnCancel.Click += (_, _) => CancelPicker();
        _cmbFileType.SelectedIndexChanged += (_, _) => RenderRows();
        _txtPath.HandleCreated += (_, _) => ApplyAddressTextBoxNativeFont();
        _txtPath.HandleDestroyed += (_, _) => DisposeAddressTextBoxNativeFont();
        _txtPath.KeyDown += TxtPath_KeyDown;
        _txtPath.Leave += TxtPath_Leave;
        _pathHost.MouseDown += AddressHost_MouseDown;
        _addressLinkPanel.MouseDown += AddressLinkPanel_MouseDown;
        _addressLinkPanel.Resize += AddressLinkPanel_Resize;
        _splitMain.MouseDown += SplitMain_MouseDown;
        _splitMain.MouseMove += SplitMain_MouseMove;
        _splitMain.MouseUp += SplitMain_MouseUp;
        _splitMain.MouseLeave += SplitMain_MouseLeave;
        _tvNav.BeforeExpand += TvNav_BeforeExpand;
        _tvNav.NodeMouseClick += TvNav_NodeMouseClick;
        _tvNav.KeyDown += TvNav_KeyDown;
        _tvNav.BeforeLabelEdit += TvNav_BeforeLabelEdit;
        _tvNav.AfterLabelEdit += TvNav_AfterLabelEdit;
        _tvNav.DrawNode += TvNav_DrawNode;
        _tvNav.MouseMove += TvNav_MouseMove;
        _tvNav.MouseLeave += TvNav_MouseLeave;
        _tvNav.Enter += TvNav_FocusChanged;
        _tvNav.Leave += TvNav_FocusChanged;

        _lvItems.BeforeLabelEdit += LvItems_BeforeLabelEdit;
        _lvItems.AfterLabelEdit += LvItems_AfterLabelEdit;
        _lvItems.ItemActivate += (_, _) => ExecuteOpenSelectedListItemCommand();
        _lvItems.KeyDown += LvItems_KeyDown;
        _lvItems.MouseUp += LvItems_MouseUp;
        _lvItems.ColumnClick += LvItems_ColumnClick;
        _lvItems.DrawColumnHeader += LvItems_DrawColumnHeader;
        _lvItems.DrawItem += LvItems_DrawItem;
        _lvItems.DrawSubItem += LvItems_DrawSubItem;
        _lvItems.MouseMove += LvItems_MouseMove;
        _lvItems.MouseLeave += LvItems_MouseLeave;
        _lvItems.Enter += LvItems_FocusChanged;
        _lvItems.Leave += LvItems_FocusChanged;
        _lvItems.ItemSelectionChanged += LvItems_ItemSelectionChanged;
        _lvItems.SelectedIndexChanged += (_, _) => LvItems_SelectedIndexChanged();

        AttachListIconRefinementTriggers();
        WireShellFontHandleEvents();
        HookExtendedMouseButtons(this);

        FormClosed += (_, _) => ReleaseWindowOwnedResources();
        Layout += ExplorerShellWindow_Layout;
    }

    private void WireShellFontHandleEvents()
    {
        _btnBack.HandleCreated += (_, _) => ApplyToolbarGlyphFont(_btnBack);
        _btnForward.HandleCreated += (_, _) => ApplyToolbarGlyphFont(_btnForward);
        _btnUp.HandleCreated += (_, _) => ApplyToolbarGlyphFont(_btnUp);
        _btnRefresh.HandleCreated += (_, _) => ApplyToolbarGlyphFont(_btnRefresh);

        _lblStatus.HandleCreated += (_, _) => ApplyChromeFont(_lblStatus);
        _lblSelection.HandleCreated += (_, _) => ApplyChromeFont(_lblSelection);
        _lblFileType.HandleCreated += (_, _) => ApplyChromeFont(_lblFileType);
        _txtFileName.HandleCreated += (_, _) => ApplyChromeFont(_txtFileName);
        _cmbFileType.HandleCreated += (_, _) => ApplyChromeFont(_cmbFileType);
        _btnOk.HandleCreated += (_, _) => ApplyChromeFont(_btnOk);
        _btnCancel.HandleCreated += (_, _) => ApplyChromeFont(_btnCancel);
        _tvNav.HandleCreated += (_, _) => ApplyChromeFont(_tvNav);
        _lvItems.HandleCreated += (_, _) => ApplyChromeFont(_lvItems);

        InstallTreeDpiPrepareHook();
    }

    private void ReleaseWindowOwnedResources()
    {
        if (_windowResourcesReleased)
            return;

        _windowResourcesReleased = true;

        _presenter?.OnViewClosed();

        EndDpiRedrawFreeze();

        _treeDpiPrepareHook?.Dispose();
        _treeDpiPrepareHook = null;

        ReleaseTreeResourcesForClose();
        ReleaseListResourcesForClose();
        DisposeAddressTextBoxNativeFont();
        DisposeWindowIcon();
    }

    private bool TryBeginInvoke(Action action)
    {
        if (action == null || IsDisposed || !IsHandleCreated)
            return false;

        try
        {
            BeginInvoke(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void LvItems_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ExecuteOpenSelectedListItemCommand();
            return;
        }

        if (e.KeyCode == Keys.F2)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ExecuteRenameSelectedListItemCommand();
            return;
        }

        if (e.KeyCode == Keys.Delete)
        {
            if (!ExecuteDeleteSelectionCommand())
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode != Keys.Apps && !(e.KeyCode == Keys.F10 && e.Shift))
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;

        ListViewItem? item = _lvItems.SelectedItems.Count > 0 ? _lvItems.SelectedItems[0] : null;
        if (item != null)
        {
            ShowListItemContextMenu(item, GetListKeyboardMenuLocation(item));
            return;
        }

        ShowListBackgroundContextMenu(GetListBackgroundKeyboardMenuLocation());
    }

    private void TvNav_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F2)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ExecuteRenameSelectedTreeNodeCommand();
            return;
        }

        if (e.KeyCode == Keys.Delete)
        {
            if (!ExecuteDeleteSelectedTreeNodeCommand())
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ExecuteOpenSelectedTreeNodeCommand();
            return;
        }

        if (e.KeyCode != Keys.Apps && !(e.KeyCode == Keys.F10 && e.Shift))
            return;

        TreeNode? node = _tvNav.SelectedNode;
        if (node?.Tag is not ExplorerTreeNodeTag tag)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        ShowTreeContextMenu(tag, GetTreeKeyboardMenuLocation(node));
    }

    private void TvNav_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node?.Tag is not ExplorerTreeNodeTag tag)
            return;

        if (tag.Kind == ExplorerTreeNodeKind.Drive && tag.IsReady == false)
        {
            e.Cancel = true;
            e.Node.Nodes.Clear();

            Point mouseLocation = _tvNav.PointToClient(Cursor.Position);

            if (!IsTreeNodeActivationHit(e.Node, mouseLocation))
                _presenter.HandleTreeNodeActivate(tag);

            return;
        }

        if (tag.Kind == ExplorerTreeNodeKind.Drive && tag.IsLocked == true)
        {
            e.Cancel = true;

            Point mouseLocation = _tvNav.PointToClient(Cursor.Position);

            if (!IsTreeNodeActivationHit(e.Node, mouseLocation))
                _presenter.HandleTreeNodeActivate(tag);

            return;
        }

        if (IsTreeFolderLoading(e.Node))
        {
            if (IsProgrammaticTreeExpand(e.Node))
                return;

            e.Cancel = true;
            return;
        }

        if (tag.Kind is not (ExplorerTreeNodeKind.Drive or ExplorerTreeNodeKind.Folder) ||
            string.IsNullOrWhiteSpace(tag.Path) ||
            !IsLazyTreeNode(e.Node))
        {
            return;
        }

        e.Cancel = true;
        _presenter.LoadTreeFolderChildrenForExpansion(tag.Path);
    }

    private void TvNav_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node == null)
            return;

        if (e.Button == MouseButtons.Left)
        {
            if (!IsTreeNodeActivationHit(e.Node, e.Location))
                return;

            _tvNav.SelectedNode = e.Node;

            if (e.Node.Tag is ExplorerTreeNodeTag tag)
                _presenter.HandleTreeNodeActivate(tag);

            return;
        }

        if (e.Button != MouseButtons.Right)
            return;

        _tvNav.SelectedNode = e.Node;

        if (e.Node.Tag is ExplorerTreeNodeTag rightClickTag)
            ShowTreeContextMenu(rightClickTag, _tvNav.PointToScreen(e.Location));
    }

    private bool IsTreeNodeActivationHit(TreeNode node, Point location)
    {
        TreeViewHitTestInfo hit = _tvNav.HitTest(location);
        if (hit.Node != node)
            return false;

        TreeViewHitTestLocations activationLocations =
            TreeViewHitTestLocations.Label |
            TreeViewHitTestLocations.Image |
            TreeViewHitTestLocations.StateImage;

        return (hit.Location & activationLocations) != 0;
    }

    private void LvItems_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
            return;

        ListViewItem? item = _lvItems.GetItemAt(e.X, e.Y);
        if (item != null)
        {
            if (!item.Selected)
                _lvItems.SelectedIndices.Clear();

            item.Selected = true;
            item.Focused = true;
            ShowListItemContextMenu(item, _lvItems.PointToScreen(e.Location));
            return;
        }

        _lvItems.SelectedIndices.Clear();
        ShowListBackgroundContextMenu(_lvItems.PointToScreen(e.Location));
    }

    private void TxtPath_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        string addressText = _txtPath.Text;
        _presenter.NavigateFromAddressBar(addressText);
        ExitAddressTextMode();
    }

    private ExplorerListRow? GetSelectedRow()
    {
        if (_lvItems.SelectedItems.Count == 0)
            return null;

        return _lvItems.SelectedItems[0].Tag as ExplorerListRow;
    }

    private IReadOnlyList<string> GetSelectedTransferablePaths()
    {
        List<string> paths = [];

        foreach (ListViewItem item in _lvItems.SelectedItems)
        {
            if (item.Tag is not ExplorerListRow row)
                continue;

            if (row.Kind is not (ExplorerListRowKind.Directory or ExplorerListRowKind.File))
                continue;

            if (!string.IsNullOrWhiteSpace(row.FullPath))
                paths.Add(row.FullPath);
        }

        return paths;
    }

    private IReadOnlyList<(string Path, bool IsTreePath)> GetSelectedTransferableCutPaths()
    {
        List<(string Path, bool IsTreePath)> paths = [];

        foreach (ListViewItem item in _lvItems.SelectedItems)
        {
            if (item.Tag is not ExplorerListRow row)
                continue;

            if (row.Kind is not (ExplorerListRowKind.Directory or ExplorerListRowKind.File))
                continue;

            if (!string.IsNullOrWhiteSpace(row.FullPath))
                paths.Add((row.FullPath, row.Kind == ExplorerListRowKind.Directory));
        }

        return paths;
    }

    private IReadOnlyList<string> GetSelectedDeletablePaths()
    {
        return GetSelectedTransferablePaths();
    }

    private bool ExecuteCopySelectionCommand()
    {
        IReadOnlyList<string> selectedPaths = GetSelectedTransferablePaths();
        if (selectedPaths.Count == 0)
            return false;

        ExplorerCommandContext context = _presenter.CreateSelectionCommandContext(selectedPaths);
        bool handled = _presenter.ExecuteContextCommand(ExplorerCommandId.Copy, context, static () => { });

        if (handled)
            ClearCutGhostedPaths();

        return handled;
    }

    private bool ExecuteCutSelectionCommand()
    {
        IReadOnlyList<string> selectedPaths = GetSelectedTransferablePaths();
        if (selectedPaths.Count == 0)
            return false;

        ExplorerCommandContext context = _presenter.CreateSelectionCommandContext(selectedPaths);
        bool handled = _presenter.ExecuteContextCommand(ExplorerCommandId.Cut, context, static () => { });

        if (handled)
            SetCutGhostedPaths(GetSelectedTransferableCutPaths());

        return handled;
    }

    private bool ExecutePasteToCurrentLocationCommand()
    {
        ExplorerCommandContext context = _presenter.CreateBackgroundCommandContext();
        bool handled = _presenter.ExecuteContextCommand(ExplorerCommandId.Paste, context, static () => { });

        if (handled)
            ClearCutGhostedPaths();

        return handled;
    }

    private bool ExecuteCancelCutClipboardCommand()
    {
        if (!ShouldRouteClipboardShortcutToShell() || !HasSelectedCutGhostedItem())
            return false;

        if (!_presenter.ClearClipboard())
            return false;

        ClearCutGhostedPaths();
        return true;
    }

    private bool HasSelectedCutGhostedItem()
    {
        if (_lvItems.ContainsFocus)
            return HasSelectedCutGhostedListItem();

        if (_tvNav.ContainsFocus)
            return HasSelectedCutGhostedTreeNode();

        return HasSelectedCutGhostedListItem() || HasSelectedCutGhostedTreeNode();
    }

    private bool HasSelectedCutGhostedListItem()
    {
        foreach (ListViewItem item in _lvItems.SelectedItems)
        {
            if (item.Tag is ExplorerListRow row && IsCutGhostedPath(row.FullPath))
                return true;
        }

        return false;
    }

    private bool HasSelectedCutGhostedTreeNode()
    {
        return _tvNav.SelectedNode?.Tag is ExplorerTreeNodeTag tag &&
            IsCutGhostedTreePath(tag.Path);
    }

    private void ExecuteOpenSelectedListItemCommand()
    {
        ExplorerListRow? row = GetSelectedRow();
        if (TryHandlePickerActivatedRow(row))
            return;

        _presenter.HandleListItemActivate(row);
    }

    private void ExecuteOpenSelectedTreeNodeCommand()
    {
        TreeNode? node = _tvNav.SelectedNode;
        if (node?.Tag is ExplorerTreeNodeTag tag)
            _presenter.HandleTreeNodeActivate(tag);
    }

    private void ExecuteRenameSelectedListItemCommand()
    {
        BeginRenameSelectedListItem();
    }

    private void ExecuteRenameSelectedTreeNodeCommand()
    {
        BeginRenameSelectedTreeNode();
    }

    private bool ExecuteDeleteSelectedTreeNodeCommand()
    {
        TreeNode? node = _tvNav.SelectedNode;
        if (node?.Tag is not ExplorerTreeNodeTag tag ||
            tag.Kind != ExplorerTreeNodeKind.Folder ||
            string.IsNullOrWhiteSpace(tag.Path))
        {
            return false;
        }

        ExplorerCommandContext context = _presenter.CreateTreeNodeCommandContext(tag);
        return _presenter.ExecuteContextCommand(ExplorerCommandId.Delete, context, static () => { });
    }

    private bool ExecuteDeleteSelectionCommand()
    {
        IReadOnlyList<string> deletePaths = GetSelectedDeletablePaths();
        if (deletePaths.Count == 0)
            return false;

        ExplorerCommandContext context = _presenter.CreateSelectionCommandContext(deletePaths);
        return _presenter.ExecuteContextCommand(ExplorerCommandId.Delete, context, static () => { });
    }

    private void ExecuteRefreshCurrentLocationCommand()
    {
        ExplorerCommandContext context = _presenter.CreateBackgroundCommandContext();
        _presenter.ExecuteContextCommand(ExplorerCommandId.Refresh, context, static () => { });
    }

    private bool IsInlineLabelEditActive()
    {
        if (_lvItems.IsHandleCreated && SendMessage(_lvItems.Handle, LVM_GETEDITCONTROL, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero)
            return true;

        if (_tvNav.IsHandleCreated && SendMessage(_tvNav.Handle, TVM_GETEDITCONTROL, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero)
            return true;

        return false;
    }

    private bool ShouldRouteClipboardShortcutToShell()
    {
        if (_txtPath.Focused)
            return false;

        if (IsPickerMode && _txtFileName.Visible && _txtFileName.Focused)
            return false;

        return !IsInlineLabelEditActive();
    }

    private bool HandleClipboardShortcut(Keys keyData)
    {
        if (!ShouldRouteClipboardShortcutToShell())
            return false;

        if (keyData == (Keys.Control | Keys.C))
            return ExecuteCopySelectionCommand();

        if (keyData == (Keys.Control | Keys.X))
            return ExecuteCutSelectionCommand();

        if (keyData == (Keys.Control | Keys.V))
            return ExecutePasteToCurrentLocationCommand();

        return false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (IsPickerMode && keyData == Keys.Escape)
        {
            CancelPicker();
            return true;
        }

        if (IsPickerMode && keyData == Keys.Enter && (_txtFileName.Focused || _cmbFileType.Focused))
        {
            ExecutePickerOkCommand();
            return true;
        }

        if (keyData == Keys.Escape && ExecuteCancelCutClipboardCommand())
            return true;

        if (HandleClipboardShortcut(keyData))
            return true;

        if (keyData == Keys.F5)
        {
            ExecuteRefreshCurrentLocationCommand();
            return true;
        }

        if (keyData == (Keys.Alt | Keys.Left))
        {
            _presenter.NavigateBack();
            return true;
        }

        if (keyData == (Keys.Alt | Keys.Right))
        {
            _presenter.NavigateForward();
            return true;
        }

        if (keyData == (Keys.Alt | Keys.Up))
        {
            _presenter.NavigateUp();
            return true;
        }

        if (keyData == Keys.Back && !_txtPath.Focused)
        {
            _presenter.NavigateUp();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
