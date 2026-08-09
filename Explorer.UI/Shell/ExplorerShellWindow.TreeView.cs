using System.Runtime.InteropServices;
using Shared.Shell.Interop;
using Shared.Shell.Models;
using Shared.Shell.Theming;
using Shell.Core.Models;

namespace Explorer.UI.Shell;

public partial class ExplorerShellWindow
{
    private TreeNode? _treeEditingNode;
    private TreeNode? _treeHoverNode;
    private string? _programmaticTreeExpandPath;

    private readonly HashSet<string> _treeLoadingPaths = new(StringComparer.OrdinalIgnoreCase);

    private const string PlaceholderText = "...";

    private const int TreeViewFirst = 0x1100;
    private const int TreeViewSetItem = TreeViewFirst + 63;
    private const uint TreeViewItemChildren = 0x0040;

    internal void ShowTree(IReadOnlyList<DriveSnapshot> drives, string currentPath)
    {
        if (_tvNav.IsDisposed)
            return;

        bool treeUpdateStarted = BeginTreeUpdateIfDpiRedrawNotFrozen(_tvNav);
        try
        {
            _treeHoverNode = null;
            ClearTreeLoadingPaths();
            _tvNav.Nodes.Clear();

            string thisPcImageKey = EnsureThisPcTreeImageKey();

            TreeNode thisPcNode = new("This PC")
            {
                Tag = new ExplorerTreeNodeTag
                {
                    Kind = ExplorerTreeNodeKind.ThisPc
                },
                ImageKey = thisPcImageKey,
                SelectedImageKey = thisPcImageKey
            };

            TreeNode nodeToSelect = thisPcNode;
            string? driveRootToSelect = GetDriveRootForTreeSelection(currentPath);

            foreach (DriveSnapshot drive in drives)
            {
                TreeNode driveNode = CreateDriveTreeNode(drive);
                thisPcNode.Nodes.Add(driveNode);

                if (!string.IsNullOrWhiteSpace(driveRootToSelect) &&
                    string.Equals(drive.DriveRoot, driveRootToSelect, StringComparison.OrdinalIgnoreCase))
                {
                    nodeToSelect = driveNode;
                }
            }

            _tvNav.Nodes.Add(thisPcNode);
            thisPcNode.Expand();
            _tvNav.SelectedNode = nodeToSelect;
            nodeToSelect.EnsureVisible();
        }
        finally
        {
            EndTreeUpdateIfStarted(_tvNav, treeUpdateStarted);
        }
    }

    internal void UpdateTreeDrive(DriveSnapshot drive, string currentPath)
    {
        if (_tvNav.IsDisposed || drive == null || string.IsNullOrWhiteSpace(drive.DriveRoot))
            return;

        TreeNode? driveNode = FindDriveRootTreeNode(drive.DriveRoot);
        if (driveNode == null)
            return;

        string driveImageKey = EnsureDriveTreeImageKey(drive);

        bool wasReady = driveNode.Tag is ExplorerTreeNodeTag existingTag &&
                existingTag.IsReady != false;

        bool displayNameChanged = !string.Equals(driveNode.Text, drive.DisplayName, StringComparison.Ordinal);
        if (displayNameChanged)
            driveNode.Text = drive.DisplayName;

        driveNode.Tag = new ExplorerTreeNodeTag
        {
            Kind = ExplorerTreeNodeKind.Drive,
            Path = drive.DriveRoot,
            DriveType = drive.DriveType,
            IsReady = drive.IsReady,
            IsLocked = drive.IsEffectivelyBitLockerLocked,
            IsBitLockerProtected = drive.IsBitLockerProtected,
            IssueKind = drive.IssueKind,
            IssueHResult = drive.IssueHResult,
            IssueMessage = drive.IssueMessage
        };

        bool imageChanged = false;

        if (!string.Equals(driveNode.ImageKey, driveImageKey, StringComparison.OrdinalIgnoreCase))
        {
            driveNode.ImageKey = driveImageKey;
            imageChanged = true;
        }

        if (!string.Equals(driveNode.SelectedImageKey, driveImageKey, StringComparison.OrdinalIgnoreCase))
        {
            driveNode.SelectedImageKey = driveImageKey;
            imageChanged = true;
        }

        if (imageChanged)
            InvalidateTreeNodeChrome(driveNode);

        if (displayNameChanged &&
            PathsEqualForTree(GetDriveRootForTreeSelection(currentPath) ?? string.Empty, drive.DriveRoot))
        {
            ResetAddressLinkRenderCache();
        }

        if (drive.IsReady == false)
        {
            driveNode.Nodes.Clear();
        }
        else if (!wasReady && driveNode.Nodes.Count == 0)
        {
            EnsurePlaceholderChild(driveNode);
        }

        if (PathsEqualForTree(GetDriveRootForTreeSelection(currentPath) ?? string.Empty, drive.DriveRoot))
            driveNode.EnsureVisible();
    }

    internal bool PrepareTreeFolderChildrenRefresh(string parentPath)
    {
        if (_tvNav.IsDisposed || string.IsNullOrWhiteSpace(parentPath))
            return false;

        TreeNode? parentNode = FindTreeNodeByPath(parentPath);
        if (parentNode == null || IsTreeFolderLoading(parentNode))
            return false;

        if (parentNode.IsExpanded)
            return !IsLazyTreeNode(parentNode);

        if (IsLazyTreeNode(parentNode))
            return false;

        ResetTreeNodeToLazyState(parentNode);

        return false;
    }

    internal void ShowTreeFolderLoading(string parentPath)
    {
        if (_tvNav.IsDisposed)
            return;

        TreeNode? parentNode = FindTreeNodeByPath(parentPath);
        if (parentNode == null)
            return;

        AddTreeLoadingPath(parentPath);

        parentNode.Nodes.Clear();
        parentNode.Collapse();
        parentNode.EnsureVisible();

        InvalidateTreeNode(parentNode);
    }

    internal void ShowTreeFolderChildren(string parentPath, IReadOnlyList<ExplorerDirectoryItem> directories)
    {
        if (_tvNav.IsDisposed)
            return;

        TreeNode? parentNode = FindTreeNodeByPath(parentPath);
        if (parentNode == null)
        {
            RemoveTreeLoadingPath(parentPath);
            return;
        }

        ShowTreeFolderChildrenCore(parentPath, parentNode, directories, CreateFolderTreeNode);
    }

    internal void ShowTreeFolderChildrenFromListRows(string parentPath, IReadOnlyList<ExplorerListRow> rows)
    {
        if (_tvNav.IsDisposed)
            return;

        TreeNode? parentNode = FindTreeNodeByPath(parentPath);
        if (parentNode == null)
        {
            RemoveTreeLoadingPath(parentPath);
            return;
        }

        ShowTreeFolderChildrenCore(
            parentPath,
            parentNode,
            GetTreeDirectoryRowsFromListRows(rows),
            CreateFolderTreeNode);
    }

    internal void UpdateTreePathChildHintFromListRows(string parentPath, IReadOnlyList<ExplorerListRow> rows)
    {
        if (_tvNav.IsDisposed || string.IsNullOrWhiteSpace(parentPath))
            return;

        TreeNode? parentNode = FindTreeNodeByPath(parentPath);
        if (parentNode?.Tag is not ExplorerTreeNodeTag tag ||
            tag.Kind is not (ExplorerTreeNodeKind.Drive or ExplorerTreeNodeKind.Folder) ||
            string.IsNullOrWhiteSpace(tag.Path) ||
            IsTreeFolderLoading(parentNode))
        {
            return;
        }

        bool hasChildDirectories = false;
        foreach (ExplorerListRow row in rows)
        {
            if (row.Kind != ExplorerListRowKind.Directory)
                continue;

            hasChildDirectories = true;
            break;
        }

        if (hasChildDirectories)
        {
            if (tag.Kind == ExplorerTreeNodeKind.Folder)
            {
                if (parentNode.Nodes.Count == 0)
                    tag.TreeChildrenLoaded = false;

                SetNativeFolderHasChildren(parentNode, hasChildren: true);
            }
            else
            {
                EnsurePlaceholderChild(parentNode);
            }

            InvalidateTreeNode(parentNode);
            return;
        }

        parentNode.Nodes.Clear();

        if (tag.Kind == ExplorerTreeNodeKind.Folder)
        {
            tag.TreeChildrenLoaded = true;
            SetNativeFolderHasChildren(parentNode, hasChildren: false);
        }

        if (parentNode.IsExpanded)
            parentNode.Collapse();

        InvalidateTreeNode(parentNode);
    }

    private void ShowTreeFolderChildrenCore<TDirectory>(
        string parentPath,
        TreeNode parentNode,
        IReadOnlyList<TDirectory> directories,
        Func<TDirectory, string, string, Color, Color, bool, TreeNode> createFolderTreeNode)
    {
        bool loadingPathRemoved = false;

        void RemoveLoadingPathOnce()
        {
            if (loadingPathRemoved)
                return;

            RemoveTreeLoadingPath(parentPath);
            loadingPathRemoved = true;
        }

        try
        {
            parentNode.Nodes.Clear();

            string normalFolderImageKey = EnsureFolderTreeImageKey(false);
            string hiddenFolderImageKey = EnsureFolderTreeImageKey(true);
            Color normalTextColor = _tvNav.ForeColor;
            Color hiddenTextColor = ShellTheme.MutedText;
            bool checkCutGhostedChildren = HasCutGhostedTreeChildrenInFolder(parentPath);

            if (parentNode.Tag is ExplorerTreeNodeTag parentTag)
                parentTag.TreeChildrenLoaded = true;

            if (directories.Count > 0)
            {
                TreeNode[] childNodes = new TreeNode[directories.Count];

                for (int i = 0; i < directories.Count; i++)
                {
                    childNodes[i] = createFolderTreeNode(
                        directories[i],
                        normalFolderImageKey,
                        hiddenFolderImageKey,
                        normalTextColor,
                        hiddenTextColor,
                        checkCutGhostedChildren);
                }

                parentNode.Nodes.AddRange(childNodes);

                // Native cChildren requires the nodes to be attached first.
                foreach (TreeNode childNode in childNodes)
                    SetNativeFolderHasChildren(childNode, hasChildren: true);

                SetNativeFolderHasChildren(parentNode, hasChildren: true);

                _programmaticTreeExpandPath = parentPath;

                try
                {
                    parentNode.Expand();
                }
                finally
                {
                    _programmaticTreeExpandPath = null;
                }
            }
            else
            {
                SetNativeFolderHasChildren(parentNode, hasChildren: false);
                parentNode.Collapse();
            }
        }
        finally
        {
            RemoveLoadingPathOnce();
        }

        InvalidateTreeNode(parentNode);
    }

    private static List<ExplorerListRow> GetTreeDirectoryRowsFromListRows(
        IReadOnlyList<ExplorerListRow> rows)
    {
        List<ExplorerListRow> directories = [];

        foreach (ExplorerListRow row in rows)
        {
            if (row.Kind == ExplorerListRowKind.Directory)
                directories.Add(row);
        }

        directories.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName));

        return directories;
    }

    internal void SelectBestExistingTreeNodeForPath(string? path)
    {
        if (_tvNav.IsDisposed)
            return;

        TreeNode? node = FindBestExistingTreeNodeForPath(path);
        if (node == null)
            return;

        if (!ReferenceEquals(_tvNav.SelectedNode, node))
            _tvNav.SelectedNode = node;

        if (!node.IsVisible)
            node.EnsureVisible();
    }

    internal void RestoreBestExistingTreeSelectionForPath(string? path)
    {
        if (_tvNav.IsDisposed)
            return;

        TreeNode? node = FindBestExistingTreeNodeForPath(path);
        if (node == null || ReferenceEquals(_tvNav.SelectedNode, node))
            return;

        TreeNode? previousTopNode = _tvNav.TopNode;
        TreeNode? previousSelectedNode = _tvNav.SelectedNode;

        _tvNav.SelectedNode = node;

        if (previousTopNode != null && previousTopNode.TreeView == _tvNav)
            _tvNav.TopNode = previousTopNode;

        InvalidateTreeNode(previousSelectedNode);
        InvalidateTreeNode(node);
    }

    internal void CancelTreeFolderLoading(string parentPath)
    {
        if (_tvNav.IsDisposed || !RemoveTreeLoadingPath(parentPath))
            return;

        TreeNode? parentNode = FindTreeNodeByPath(parentPath);
        if (parentNode == null)
            return;

        ResetTreeNodeToLazyState(parentNode);
        parentNode.Collapse();

        InvalidateTreeNode(parentNode);
    }

    private TreeNode CreateDriveTreeNode(DriveSnapshot drive)
    {
        string driveImageKey = EnsureDriveTreeImageKey(drive);
        if (IsCutGhostedTreePath(drive.DriveRoot))
            driveImageKey = EnsureGhostedTreeImageKey(driveImageKey);

        TreeNode driveNode = new(drive.DisplayName)
        {
            Tag = new ExplorerTreeNodeTag
            {
                Kind = ExplorerTreeNodeKind.Drive,
                Path = drive.DriveRoot,
                DriveType = drive.DriveType,
                IsReady = drive.IsReady,
                IsLocked = drive.IsEffectivelyBitLockerLocked,
                IsBitLockerProtected = drive.IsBitLockerProtected,
                IssueKind = drive.IssueKind,
                IssueHResult = drive.IssueHResult,
                IssueMessage = drive.IssueMessage
            },
            ImageKey = driveImageKey,
            SelectedImageKey = driveImageKey
        };

        EnsurePlaceholderChild(driveNode);
        return driveNode;
    }

    private TreeNode CreateFolderTreeNode(
        ExplorerDirectoryItem directory,
        string normalFolderImageKey,
        string hiddenFolderImageKey,
        Color normalTextColor,
        Color hiddenTextColor,
        bool checkCutGhostedPath)
    {
        return CreateFolderTreeNodeCore(
            directory.Name,
            directory.FullPath,
            directory.IsVisibleHidden,
            normalFolderImageKey,
            hiddenFolderImageKey,
            normalTextColor,
            hiddenTextColor,
            checkCutGhostedPath);
    }

    private TreeNode CreateFolderTreeNode(
        ExplorerListRow row,
        string normalFolderImageKey,
        string hiddenFolderImageKey,
        Color normalTextColor,
        Color hiddenTextColor,
        bool checkCutGhostedPath)
    {
        return CreateFolderTreeNodeCore(
            row.DisplayName,
            row.FullPath,
            row.IsVisibleHidden,
            normalFolderImageKey,
            hiddenFolderImageKey,
            normalTextColor,
            hiddenTextColor,
            checkCutGhostedPath);
    }

    private TreeNode CreateFolderTreeNodeCore(
        string name,
        string fullPath,
        bool isVisibleHidden,
        string normalFolderImageKey,
        string hiddenFolderImageKey,
        Color normalTextColor,
        Color hiddenTextColor,
        bool checkCutGhostedPath)
    {
        string imageKey = isVisibleHidden
            ? hiddenFolderImageKey
            : normalFolderImageKey;

        if (checkCutGhostedPath && IsCutGhostedTreePath(fullPath))
            imageKey = EnsureGhostedTreeImageKey(imageKey);

        TreeNode node = new(name)
        {
            Tag = new ExplorerTreeNodeTag
            {
                Kind = ExplorerTreeNodeKind.Folder,
                Path = fullPath
            },
            ImageKey = imageKey,
            SelectedImageKey = imageKey,
            ForeColor = normalTextColor
        };

        return node;
    }

    private static bool ShouldSuppressTreeExpandPlaceholder(ExplorerTreeNodeTag tag)
    {
        return tag.Kind == ExplorerTreeNodeKind.Drive &&
               tag.IsReady == false;
    }

    private void EnsurePlaceholderChild(TreeNode node)
    {
        if (node.Tag is not ExplorerTreeNodeTag tag ||
            tag.Kind is not (ExplorerTreeNodeKind.Drive or ExplorerTreeNodeKind.Folder) ||
            string.IsNullOrWhiteSpace(tag.Path) ||
            node.Nodes.Count > 0 ||
            ShouldSuppressTreeExpandPlaceholder(tag))
        {
            return;
        }

        node.Nodes.Add(new TreeNode(PlaceholderText));
    }

    private static bool HasLazyChild(TreeNode node)
    {
        return node.Nodes.Count == 1 &&
               string.Equals(node.Nodes[0].Text, PlaceholderText, StringComparison.Ordinal);
    }

    private static bool IsNativeLazyFolderNode(TreeNode node)
    {
        return node.Tag is ExplorerTreeNodeTag tag &&
               tag.Kind == ExplorerTreeNodeKind.Folder &&
               !tag.TreeChildrenLoaded &&
               !string.IsNullOrWhiteSpace(tag.Path) &&
               node.Nodes.Count == 0;
    }

    private static bool IsLazyTreeNode(TreeNode node)
    {
        return HasLazyChild(node) || IsNativeLazyFolderNode(node);
    }

    private void ResetTreeNodeToLazyState(TreeNode node)
    {
        if (node.Tag is not ExplorerTreeNodeTag tag ||
            tag.Kind is not (ExplorerTreeNodeKind.Drive or ExplorerTreeNodeKind.Folder) ||
            string.IsNullOrWhiteSpace(tag.Path))
        {
            node.Nodes.Clear();
            return;
        }

        node.Nodes.Clear();

        if (tag.Kind == ExplorerTreeNodeKind.Folder)
        {
            tag.TreeChildrenLoaded = false;
            SetNativeFolderHasChildren(node, hasChildren: true);
            return;
        }

        EnsurePlaceholderChild(node);
    }

    private void SetNativeFolderHasChildren(TreeNode node, bool hasChildren)
    {
        if (node.Tag is not ExplorerTreeNodeTag tag ||
            tag.Kind != ExplorerTreeNodeKind.Folder ||
            node.TreeView == null ||
            node.TreeView.IsDisposed ||
            node.Handle == IntPtr.Zero)
        {
            return;
        }

        TreeViewItem item = new()
        {
            Mask = TreeViewItemChildren,
            ItemHandle = node.Handle,
            Children = hasChildren ? 1 : 0
        };

        IntPtr itemPtr = Marshal.AllocHGlobal(Marshal.SizeOf<TreeViewItem>());
        try
        {
            Marshal.StructureToPtr(item, itemPtr, false);
            User32.SendMessage(node.TreeView.Handle, TreeViewSetItem, IntPtr.Zero, itemPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(itemPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TreeViewItem
    {
        public uint Mask;
        public IntPtr ItemHandle;
        public uint State;
        public uint StateMask;
        public IntPtr Text;
        public int TextMax;
        public int Image;
        public int SelectedImage;
        public int Children;
        public IntPtr Param;
    }

    private static string? GetTreeNodePath(TreeNode? node)
    {
        return node?.Tag is ExplorerTreeNodeTag tag
            ? tag.Path
            : null;
    }

    private bool IsTreeFolderLoading(TreeNode? node)
    {
        string? path = GetTreeNodePath(node);
        return !string.IsNullOrWhiteSpace(path) &&
               _treeLoadingPaths.Contains(path);
    }

    private void AddTreeLoadingPath(string parentPath)
    {
        if (string.IsNullOrWhiteSpace(parentPath))
            return;

        if (_treeLoadingPaths.Add(parentPath))
            UpdateTreeBusyCursor();
    }

    private bool RemoveTreeLoadingPath(string parentPath)
    {
        if (!_treeLoadingPaths.Remove(parentPath))
            return false;

        UpdateTreeBusyCursor();
        return true;
    }

    private void ClearTreeLoadingPaths()
    {
        if (_treeLoadingPaths.Count == 0)
            return;

        _treeLoadingPaths.Clear();
        UpdateTreeBusyCursor();
    }

    internal void ReleaseTreeResourcesForDpiChange()
    {
        ReleaseTreeResources(clearSelectionBeforeClear: false);
    }

    private void ReleaseTreeResourcesForClose()
    {
        ReleaseTreeResources(clearSelectionBeforeClear: true);
    }

    private void ReleaseTreeResources(bool clearSelectionBeforeClear)
    {
        ClearTreeLoadingPaths();
        _programmaticTreeExpandPath = null;
        _treeEditingNode = null;
        _treeHoverNode = null;

        if (_tvNav.IsDisposed)
            return;

        bool treeUpdateStarted = BeginTreeUpdateIfDpiRedrawNotFrozen(_tvNav);
        try
        {
            // During DPI prep the tree is already externally redraw-frozen and
            // Nodes.Clear() will clear the selection anyway. Avoid the extra
            // native selection reset in the DPI hot path, but keep the old
            // explicit close behavior.
            if (clearSelectionBeforeClear)
                _tvNav.SelectedNode = null;

            _tvNav.Nodes.Clear();
        }
        finally
        {
            EndTreeUpdateIfStarted(_tvNav, treeUpdateStarted);
        }
    }

    private bool BeginTreeUpdateIfDpiRedrawNotFrozen(TreeView treeView)
    {
        if (_dpiRedrawFreezeActive || treeView.IsDisposed)
            return false;

        treeView.BeginUpdate();
        return true;
    }

    private static void EndTreeUpdateIfStarted(TreeView treeView, bool started)
    {
        if (!started || treeView.IsDisposed)
            return;

        treeView.EndUpdate();
    }

    private void UpdateTreeBusyCursor()
    {
        if (_tvNav.IsDisposed)
            return;

        bool isBusy = _treeLoadingPaths.Count > 0;

        _tvNav.UseWaitCursor = isBusy;
        _tvNav.Cursor = isBusy
            ? Cursors.WaitCursor
            : Cursors.Default;
    }

    private bool IsProgrammaticTreeExpand(TreeNode? node)
    {
        string? path = GetTreeNodePath(node);

        return !string.IsNullOrWhiteSpace(path) &&
               !string.IsNullOrWhiteSpace(_programmaticTreeExpandPath) &&
               PathsEqualForTree(path, _programmaticTreeExpandPath);
    }

    private void TvNav_DrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (sender is not TreeView treeView || e.Node == null)
        {
            e.DrawDefault = true;
            return;
        }

        bool isEditing = ReferenceEquals(e.Node, _treeEditingNode);

        bool isSelected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;
        bool isActiveSelection = isSelected && IsTreeSelectionActive(treeView);
        bool isInactiveSelection = isSelected && !isActiveSelection;
        bool isHover = !isSelected && ReferenceEquals(e.Node, _treeHoverNode);
        Rectangle bounds = e.Bounds;

        if (bounds.Width > 0 && bounds.Height > 0)
        {
            Color backColor = isActiveSelection
                ? ShellTheme.ItemSelectedBack
                : isInactiveSelection || isHover
                    ? ShellTheme.ItemHoverBack
                    : treeView.BackColor;

            using SolidBrush backBrush = new(backColor);
            e.Graphics.FillRectangle(backBrush, bounds);
        }

        if (isEditing)
        {
            e.DrawDefault = false;
            return;
        }

        Color textColor = GetTreeNodeTextColor(treeView, e.Node, isActiveSelection);

        TextRenderer.DrawText(
            e.Graphics,
            e.Node.Text,
            treeView.Font,
            bounds,
            textColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        e.DrawDefault = false;
    }

    private Color GetTreeNodeTextColor(TreeView treeView, TreeNode node, bool isActiveSelection)
    {
        if (node.Tag is ExplorerTreeNodeTag tag && IsCutGhostedTreePath(tag.Path))
            return ShellTheme.ItemCutText;

        if (isActiveSelection || ReferenceEquals(treeView.SelectedNode, node))
            return ShellTheme.ItemSelectedText;

        if (!node.ForeColor.IsEmpty)
            return node.ForeColor;

        return treeView.ForeColor;
    }

    private void ApplyCutGhostedTreeIconState(IEnumerable<string> previousTreePaths)
    {
        if (_tvNav.IsDisposed)
            return;

        HashSet<string> pathsToUpdate = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in previousTreePaths)
        {
            string? normalizedPath = NormalizeGhostedPath(path);
            if (normalizedPath != null)
                pathsToUpdate.Add(normalizedPath);
        }

        foreach (string path in _cutGhostedTreePaths)
            pathsToUpdate.Add(path);

        foreach (string path in pathsToUpdate)
        {
            TreeNode? node = FindTreeNodeByPath(path);
            if (node != null)
                ApplyCutGhostedTreeIconState(node);
        }

    }

    private void ApplyCutGhostedTreeIconState(TreeNode node)
    {
        if (node.Tag is not ExplorerTreeNodeTag tag || string.IsNullOrWhiteSpace(tag.Path))
            return;

        string baseImageKey = RemoveCutGhostedImageKeySuffix(node.ImageKey ?? string.Empty);
        string desiredImageKey = IsCutGhostedTreePath(tag.Path)
                    ? EnsureGhostedTreeImageKey(baseImageKey)
                    : baseImageKey;

        if (!string.Equals(node.ImageKey, desiredImageKey, StringComparison.OrdinalIgnoreCase))
        {
            node.ImageKey = desiredImageKey;
            node.SelectedImageKey = desiredImageKey;
        }

        InvalidateTreeNode(node);
    }

    private static bool IsTreeSelectionActive(TreeView treeView)
    {
        return treeView.Focused || treeView.ContainsFocus;
    }

    private void TvNav_FocusChanged(object? sender, EventArgs e)
    {
        InvalidateTreeNode(_tvNav.SelectedNode);
    }

    private void TvNav_MouseMove(object? sender, MouseEventArgs e)
    {
        SetTreeHoverNode(_tvNav.GetNodeAt(e.Location));
    }

    private void TvNav_MouseLeave(object? sender, EventArgs e)
    {
        SetTreeHoverNode(null);
    }

    private void SetTreeHoverNode(TreeNode? node)
    {
        if (ReferenceEquals(_treeHoverNode, node))
            return;

        TreeNode? oldNode = _treeHoverNode;
        _treeHoverNode = node;

        InvalidateTreeNode(oldNode);
        InvalidateTreeNode(_treeHoverNode);
    }

    private void InvalidateTreeNode(TreeNode? node)
    {
        if (node == null || _tvNav.IsDisposed || !_tvNav.IsHandleCreated)
            return;

        Rectangle bounds = node.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        const int leftPadding = 2;
        const int verticalPadding = 1;
        int rightPadding = GetTreeTextInvalidateRightPadding();

        int left = Math.Max(0, bounds.Left - leftPadding);
        int right = Math.Min(_tvNav.ClientSize.Width, bounds.Right + rightPadding);
        if (right <= left)
            return;

        bounds = new Rectangle(
            left,
            bounds.Top - verticalPadding,
            right - left,
            bounds.Height + (verticalPadding * 2));

        _tvNav.Invalidate(bounds);
    }

    private void InvalidateTreeNodeChrome(TreeNode? node)
    {
        if (node == null || _tvNav.IsDisposed || !_tvNav.IsHandleCreated)
            return;

        Rectangle bounds = node.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        int iconWidth = _tvNav.ImageList?.ImageSize.Width ?? _mPx.SmallImageSize.Width;

        // TreeNode.Bounds is the text rectangle. Include the image/glyph area to
        // repaint drive icon changes immediately instead of waiting for a later
        // broader TreeView paint.
        int leftPadding = Math.Max(4, iconWidth + ScaleDip(8));
        int rightPadding = GetTreeTextInvalidateRightPadding();
        int verticalPadding = 1;

        int left = Math.Max(0, bounds.Left - leftPadding);
        int right = Math.Min(_tvNav.ClientSize.Width, bounds.Right + rightPadding);
        if (right <= left)
            return;

        _tvNav.Invalidate(new Rectangle(
            left,
            Math.Max(0, bounds.Top - verticalPadding),
            right - left,
            Math.Min(_tvNav.ClientSize.Height - Math.Max(0, bounds.Top - verticalPadding),
                bounds.Height + (verticalPadding * 2))));
    }

    private int GetTreeTextInvalidateRightPadding()
    {
        int dpi = _tvNav.DeviceDpi;

        if (dpi >= 144)
            return Math.Max(7, ScaleDip(4) + 1);

        if (dpi >= 120)
            return 4;

        return 2;
    }

    private TreeNode? FindBestExistingTreeNodeForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.Equals(path, ExplorerShellWindowPresenter.ThisPcPath, StringComparison.Ordinal))
        {
            return _tvNav.Nodes.Count == 0 ? null : _tvNav.Nodes[0];
        }

        string? driveRoot = GetDriveRootForTreeSelection(path);
        if (string.IsNullOrWhiteSpace(driveRoot))
            return null;

        TreeNode? bestNode = FindDriveRootTreeNode(driveRoot);
        if (bestNode == null)
            return null;

        string relativePath = path.Substring(driveRoot.Length)
            .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(relativePath))
            return bestNode;

        string currentPath = driveRoot;

        foreach (string segment in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);

            TreeNode? nextNode = FindTreeNodeByPath(currentPath);
            if (nextNode == null)
                break;

            bestNode = nextNode;
        }

        return bestNode;
    }

    private TreeNode? FindDriveRootTreeNode(string driveRoot)
    {
        if (_tvNav.Nodes.Count == 0)
            return null;

        foreach (TreeNode node in _tvNav.Nodes[0].Nodes)
        {
            if (node.Tag is ExplorerTreeNodeTag tag &&
                tag.Kind == ExplorerTreeNodeKind.Drive &&
                !string.IsNullOrWhiteSpace(tag.Path) &&
                PathsEqualForTree(tag.Path, driveRoot))
            {
                return node;
            }
        }

        return null;
    }

    private TreeNode? FindTreeNodeByPath(string path)
    {
        return FindTreeNodeByPath(_tvNav.Nodes, path);
    }

    private static TreeNode? FindTreeNodeByPath(TreeNodeCollection nodes, string path)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is ExplorerTreeNodeTag tag &&
                !string.IsNullOrWhiteSpace(tag.Path))
            {
                if (PathsEqualForTree(tag.Path, path))
                    return node;

                if (!IsTreePathAncestorOfTarget(tag.Path, path))
                    continue;
            }

            if (node.Nodes.Count == 0 || HasLazyChild(node))
                continue;

            TreeNode? found = FindTreeNodeByPath(node.Nodes, path);
            if (found != null)
                return found;
        }

        return null;
    }

    private static bool IsTreePathAncestorOfTarget(string nodePath, string targetPath)
    {
        string normalizedNodePath = NormalizeTreePathForPrefix(nodePath);
        string normalizedTargetPath = NormalizeTreePathForPrefix(targetPath);

        return normalizedTargetPath.StartsWith(
            normalizedNodePath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTreePathForPrefix(string path)
    {
        string normalized = path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return normalized + Path.DirectorySeparatorChar;
    }

    private static bool PathsEqualForTree(string left, string right)
    {
        static string Normalize(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetDriveRootForTreeSelection(string currentPath)
    {
        if (string.Equals(currentPath, ExplorerShellWindowPresenter.ThisPcPath, StringComparison.Ordinal))
            return null;

        try
        {
            return Path.GetPathRoot(currentPath);
        }
        catch
        {
            return null;
        }
    }
}