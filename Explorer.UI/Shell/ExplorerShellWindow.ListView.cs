using Shared.Shell.Interop;
using Shared.Shell.Theming;
using Shell.Core.Models;
using System.Globalization;

namespace Explorer.UI.Shell;

public partial class ExplorerShellWindow
{
    private IReadOnlyList<ExplorerListRow> _currentRows = Array.Empty<ExplorerListRow>();
    private bool _isShowingDriveRows;
    private int _sortColumn;
    private SortOrder _sortOrder = SortOrder.Ascending;
    private int _listIconRefineVersion;
    private int _pendingListIconRefineVersion;
    private bool _listIconRefineQueued;
    private ListViewItem? _listHoverItem;
    private readonly HashSet<string> _cutGhostedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _cutGhostedTreePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _cutGhostedTreeParentPaths = new(StringComparer.OrdinalIgnoreCase);
    private ListViewViewportWatcher? _listViewViewportWatcher;
    private ThemedListViewHeaderWatcher? _listViewHeaderWatcher;

    private enum ListColumnMode
    {
        None,
        Drive,
        Directory
    }

    private ListColumnMode _configuredColumnMode;
    private int _configuredColumnDpi;

    internal void ShowDriveRows(IReadOnlyList<ExplorerListRow> rows)
    {
        bool modeChanged = !_isShowingDriveRows;

        _isShowingDriveRows = true;
        _currentRows = rows ?? Array.Empty<ExplorerListRow>();

        if (modeChanged)
            ResetSort();

        EnsureDriveColumnsConfigured();
        RenderRows();
    }

    internal void ShowDirectoryRows(IReadOnlyList<ExplorerListRow> rows)
    {
        bool modeChanged = _isShowingDriveRows;

        _isShowingDriveRows = false;
        _currentRows = rows ?? Array.Empty<ExplorerListRow>();

        if (modeChanged)
            ResetSort();

        EnsureDirectoryColumnsConfigured();
        RenderRows();
    }

    private void ResetSort()
    {
        _sortColumn = 0;
        _sortOrder = SortOrder.Ascending;
    }

    private void RenderRows()
    {
        int refineVersion = ++_listIconRefineVersion;
        List<ExplorerListRow> rows = GetSortedRows();

        ListViewItem[] items = new ListViewItem[rows.Count];

        for (int index = 0; index < rows.Count; index++)
        {
            ExplorerListRow row = rows[index];
            ListViewItem item = BuildListViewItem(row);
            items[index] = item;
        }

        _lvItems.BeginUpdate();
        try
        {
            _listHoverItem = null;
            _lvItems.Items.Clear();
            ClearPathSpecificListImages();

            if (items.Length > 0)
                _lvItems.Items.AddRange(items);
        }
        finally
        {
            _lvItems.EndUpdate();
        }

        UpdateBrowseStatusTextFromSelection();
        QueuePathSpecificListIconRefinement(refineVersion);
    }

    private void LvItems_SelectedIndexChanged()
    {
        UpdateBrowseStatusTextFromSelection();
        UpdatePickerFileNameFromSelection();
    }

    private void LvItems_ItemSelectionChanged(object? sender, ListViewItemSelectionChangedEventArgs e)
    {
        // DrawSelectedListSubItemBorder suppresses the bottom border when the
        // next item is selected. During mouse-drag selection, especially when
        // selecting downward, Windows may repaint only the item whose selection
        // changed. Repaint the changed row and its neighbors so stale bottom
        // borders are removed as adjacent rows join or leave the selection.
        InvalidateListItemRow(e.Item);
        InvalidateListItemRowByIndex(e.ItemIndex - 1);
        InvalidateListItemRowByIndex(e.ItemIndex + 1);
    }


    internal void UpdateBrowseStatusTextFromSelection()
    {
        if (_mode != ExplorerWindowMode.Browse)
            return;

        int itemCount = _lvItems.Items.Count;
        int selectedCount = _lvItems.SelectedItems.Count;
        string itemText = itemCount == 1 ? "1 Item" : $"{itemCount:N0} Items";

        if (selectedCount <= 0)
        {
            SetStatusText(itemText);
            return;
        }

        string selectedText = selectedCount == 1
                    ? "1 Item Selected"
                    : $"{selectedCount:N0} Items Selected";

        SetStatusText($"{itemText} | {selectedText}");
    }

    private void QueuePathSpecificListIconRefinement(int refineVersion)
    {
        if (_isShowingDriveRows || _lvItems.IsDisposed || !IsHandleCreated)
            return;

        _pendingListIconRefineVersion = refineVersion;

        if (_listIconRefineQueued)
            return;

        _listIconRefineQueued = true;

        if (!TryBeginInvoke(() =>
            {
                _listIconRefineQueued = false;

                int pendingVersion = _pendingListIconRefineVersion;
                if (pendingVersion != _listIconRefineVersion)
                    return;

                RefineVisiblePathSpecificListIcons(pendingVersion);
            }))
        {
            _listIconRefineQueued = false;
        }
    }

    private void QueueCurrentPathSpecificListIconRefinement()
    {
        QueuePathSpecificListIconRefinement(_listIconRefineVersion);
    }

    // Keep path-specific extraction limited to the current visible viewport.
    // Refining every .exe/.ico/.lnk/.url in large folders such as System32 regresses
    // first-open performance back toward synchronous path icon extraction.
    private void RefineVisiblePathSpecificListIcons(int refineVersion)
    {
        if (refineVersion != _listIconRefineVersion || _isShowingDriveRows || _lvItems.IsDisposed)
            return;

        (int firstIndex, int lastIndex) = GetPathSpecificIconRefinementRange();
        if (firstIndex < 0 || lastIndex < firstIndex)
            return;

        _lvItems.BeginUpdate();
        try
        {
            for (int index = firstIndex; index <= lastIndex; index++)
            {
                if (refineVersion != _listIconRefineVersion)
                    return;

                if (index < 0 || index >= _lvItems.Items.Count)
                    continue;

                ListViewItem item = _lvItems.Items[index];

                if (item.Tag is not ExplorerListRow row)
                    continue;

                if (!TryEnsurePathSpecificListImageKey(row, out string imageKey))
                    continue;

                if (string.Equals(item.ImageKey, imageKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                item.ImageKey = imageKey;
            }
        }
        finally
        {
            _lvItems.EndUpdate();
        }
    }

    private (int FirstIndex, int LastIndex) GetPathSpecificIconRefinementRange()
    {
        int itemCount = _lvItems.Items.Count;
        if (itemCount == 0)
            return (-1, -1);

        int firstVisibleIndex = GetFirstVisibleListItemIndex();
        if (firstVisibleIndex < 0)
            firstVisibleIndex = 0;

        int visibleCount = EstimateVisibleListItemCount(firstVisibleIndex);
        if (visibleCount <= 0)
            return (-1, -1);

        int lastIndex = Math.Min(
            itemCount - 1,
            firstVisibleIndex + visibleCount - 1);

        return (firstVisibleIndex, lastIndex);
    }

    private int GetFirstVisibleListItemIndex()
    {
        try
        {
            return _lvItems.TopItem?.Index ?? 0;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private int EstimateVisibleListItemCount(int firstVisibleIndex)
    {
        if (_lvItems.Items.Count == 0)
            return 0;

        int itemHeight = 0;

        try
        {
            if (firstVisibleIndex >= 0 && firstVisibleIndex < _lvItems.Items.Count)
                itemHeight = _lvItems.GetItemRect(firstVisibleIndex).Height;
        }
        catch (ArgumentOutOfRangeException)
        {
            itemHeight = 0;
        }

        if (itemHeight <= 0)
        {
            int imageHeight = _lvItems.SmallImageList?.ImageSize.Height ?? SystemInformation.SmallIconSize.Height;
            itemHeight = Math.Max(1, imageHeight + 6);
        }

        return Math.Max(
            1,
            (_lvItems.ClientSize.Height + itemHeight - 1) / itemHeight);
    }

    private void AttachListIconRefinementTriggers()
    {
        _lvItems.Resize += (_, _) =>
        {
            QueueCurrentPathSpecificListIconRefinement();
            InvalidateThemedListHeader();
        };

        _lvItems.ColumnWidthChanged += (_, _) => InvalidateThemedListHeader();

        _lvItems.HandleCreated += (_, _) =>
        {
            _listViewViewportWatcher?.ReleaseHandle();
            _listViewViewportWatcher = new ListViewViewportWatcher(
                _lvItems,
                QueueCurrentPathSpecificListIconRefinement);

            AttachThemedListHeader();
        };

        _lvItems.HandleDestroyed += (_, _) =>
        {
            _listViewViewportWatcher?.ReleaseHandle();
            _listViewViewportWatcher = null;

            _listViewHeaderWatcher?.ReleaseHandle();
            _listViewHeaderWatcher = null;
        };

        if (_lvItems.IsHandleCreated)
        {
            _listViewViewportWatcher?.ReleaseHandle();
            _listViewViewportWatcher = new ListViewViewportWatcher(
                _lvItems,
                QueueCurrentPathSpecificListIconRefinement);

            AttachThemedListHeader();
        }
    }

    private List<ExplorerListRow> GetSortedRows()
    {
        IReadOnlyList<ExplorerListRow> source = _currentRows ?? Array.Empty<ExplorerListRow>();
        List<ExplorerListRow> rows = new(source.Count);

        foreach (ExplorerListRow row in source)
        {
            if (IsAllowedByCurrentFilter(row))
                rows.Add(row);
        }

        rows.Sort(new ExplorerListRowComparer(_isShowingDriveRows, _sortColumn, _sortOrder));
        return rows;
    }

    private ListViewItem BuildListViewItem(ExplorerListRow row)
    {
        ListViewItem item = new(row.DisplayName)
        {
            Tag = row,
            ImageKey = EnsureListImageKey(row)
        };

        if (_isShowingDriveRows)
        {
            item.SubItems.Add(row.DriveType?.ToString() ?? string.Empty);
            item.SubItems.Add(FormatYesNo(row.IsReady));
            item.SubItems.Add(FormatYesNo(row.IsLocked));
            item.SubItems.Add(FormatDriveCapacity(row.FreeSpaceBytes));
            item.SubItems.Add(FormatDriveCapacity(row.TotalSizeBytes));
        }
        else
        {
            item.SubItems.Add(FormatDateModified(row.ModifiedLocalTime));
            item.SubItems.Add(row.TypeText);
            item.SubItems.Add(FormatSize(row));
        }

        return item;
    }

    private void EnsureDriveColumnsConfigured()
    {
        if (_configuredColumnMode == ListColumnMode.Drive &&
            _configuredColumnDpi == DeviceDpi)
        {
            return;
        }

        ConfigureDriveColumns();
        _configuredColumnMode = ListColumnMode.Drive;
        _configuredColumnDpi = DeviceDpi;
    }

    private void EnsureDirectoryColumnsConfigured()
    {
        if (_configuredColumnMode == ListColumnMode.Directory &&
            _configuredColumnDpi == DeviceDpi)
        {
            return;
        }

        ConfigureDirectoryColumns();
        _configuredColumnMode = ListColumnMode.Directory;
        _configuredColumnDpi = DeviceDpi;
    }

    private void ConfigureDriveColumns()
    {
        _lvItems.Clear();
        _lvItems.View = View.Details;

        _lvItems.Columns.Add("Name", _mPx.ThisPcNameColumnWidth);
        _lvItems.Columns.Add("Type", _mPx.ThisPcTypeColumnWidth);
        _lvItems.Columns.Add("Ready", ScaleDip(70));
        _lvItems.Columns.Add("Locked", ScaleDip(70));
        _lvItems.Columns.Add("Free Space", _mPx.SizeColumnWidth);
        _lvItems.Columns.Add("Total Size", _mPx.SizeColumnWidth);
        AttachThemedListHeader();
        InvalidateThemedListHeader();
    }

    private void ConfigureDirectoryColumns()
    {
        _lvItems.Clear();
        _lvItems.View = View.Details;

        _lvItems.Columns.Add("Name", _mPx.NameColumnWidth);
        _lvItems.Columns.Add("Modified", _mPx.DateColumnWidth);
        _lvItems.Columns.Add("Type", _mPx.TypeColumnWidth);
        _lvItems.Columns.Add("Size", _mPx.SizeColumnWidth);
        AttachThemedListHeader();
        InvalidateThemedListHeader();
    }

    private void LvItems_DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using SolidBrush backBrush = new(ShellTheme.ContentBack);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        using Pen separatorPen = new(ShellTheme.ContentBorder);
        e.Graphics.DrawLine(separatorPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        if (e.ColumnIndex > 0)
            e.Graphics.DrawLine(separatorPen, e.Bounds.Left, e.Bounds.Top + 2, e.Bounds.Left, e.Bounds.Bottom - 3);

        Rectangle textBounds = new(
            e.Bounds.Left + ScaleDip(6),
            e.Bounds.Top,
            Math.Max(0, e.Bounds.Width - ScaleDip(12)),
            e.Bounds.Height);

        TextRenderer.DrawText(
            e.Graphics,
            e.Header?.Text ?? string.Empty,
            _lvItems.Font,
            textBounds,
            ShellTheme.TextColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }

    private void LvItems_DrawItem(object? sender, DrawListViewItemEventArgs e)
    {
        if (sender is not ListView listView || listView.View != View.Details)
        {
            e.DrawDefault = true;
            return;
        }

        // In Details view, DrawSubItem owns the actual row painting.  Do not
        // fill the row here: Windows can request DrawItem during hot tracking
        // after subitems have already painted, which erases the later columns.
        e.DrawDefault = false;
    }

    private void LvItems_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (sender is not ListView listView || listView.View != View.Details)
        {
            e.DrawDefault = true;
            return;
        }

        Color textColor = GetListSubItemTextColor(listView, e.Item, e.SubItem);

        DrawListSubItemBackground(listView, e);

        if (e.ColumnIndex == 0)
        {
            DrawListItemImage(listView, e.Item, e.Graphics);
            DrawListSubItemText(
                e.Graphics,
                e.SubItem.Text,
                e.Item.Font,
                e.Item.GetBounds(ItemBoundsPortion.Label),
                textColor);

            e.DrawDefault = false;
            return;
        }

        Rectangle textBounds = new(
            e.Bounds.Left + ScaleDip(6),
            e.Bounds.Top,
            Math.Max(0, e.Bounds.Width - ScaleDip(12)),
            e.Bounds.Height);

        DrawListSubItemText(
            e.Graphics,
            e.SubItem.Text,
            e.Item.Font,
            textBounds,
            textColor);

        e.DrawDefault = false;
    }

    private void DrawListSubItemBackground(ListView listView, DrawListViewSubItemEventArgs e)
    {
        bool isSelected = e.Item.Selected;
        bool isActiveSelection = isSelected && IsListSelectionActive(listView);
        bool isInactiveSelection = isSelected && !isActiveSelection;
        bool isHover = !isSelected && ReferenceEquals(e.Item, _listHoverItem);

        Color backColor = isActiveSelection
            ? ShellTheme.ItemSelectedBack
            : isInactiveSelection || isHover
                ? ShellTheme.ItemHoverBack
                : listView.BackColor;

        using SolidBrush backBrush = new(backColor);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        if (isActiveSelection)
            DrawSelectedListSubItemBorder(listView, e.Graphics, e.Bounds, e.Item, e.ColumnIndex);
    }

    private static void DrawSelectedListSubItemBorder(ListView listView, Graphics graphics, Rectangle bounds, ListViewItem item, int columnIndex)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        bool nextSelected =
        item.Index >= 0 &&
        item.Index < listView.Items.Count - 1 &&
        listView.Items[item.Index + 1].Selected;

        int left = bounds.Left;
        int right = bounds.Right - 1;
        int top = bounds.Top;
        int bottom = bounds.Bottom - 1;

        using Pen borderPen = new(ShellTheme.ItemSelectedBorder);
        graphics.DrawLine(borderPen, left, top, right, top);

        if (!nextSelected)
            graphics.DrawLine(borderPen, left, bottom, right, bottom);

        if (columnIndex == 0)
            graphics.DrawLine(borderPen, left, top, left, bottom);

        if (columnIndex == listView.Columns.Count - 1)
            graphics.DrawLine(borderPen, right, top, right, bottom);
    }

    private static void DrawListSubItemText(Graphics graphics, string text, Font font, Rectangle bounds, Color textColor)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        TextRenderer.DrawText(
            graphics,
            text,
            font,
            bounds,
            textColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }

    private void DrawListItemImage(ListView listView, ListViewItem item, Graphics graphics)
    {
        ImageList? imageList = listView.SmallImageList;
        if (imageList == null)
            return;

        int imageIndex = -1;
        string imageKey = item.ImageKey ?? string.Empty;

        if (IsCutGhostedPath(GetListItemPath(item)) && !string.IsNullOrEmpty(imageKey))
            imageKey = EnsureGhostedListImageKey(imageKey);
        
                if (!string.IsNullOrEmpty(imageKey))
            imageIndex = imageList.Images.IndexOfKey(imageKey);

        if (imageIndex < 0 &&
            item.ImageIndex >= 0 &&
            item.ImageIndex < imageList.Images.Count)
        {
            imageIndex = item.ImageIndex;
        }

        if (imageIndex < 0)
            return;

        Rectangle iconBounds = item.GetBounds(ItemBoundsPortion.Icon);
        if (iconBounds.Width <= 0 || iconBounds.Height <= 0)
            return;

        Size imageSize = imageList.ImageSize;
        int x = iconBounds.Left + Math.Max(0, (iconBounds.Width - imageSize.Width) / 2);
        int y = iconBounds.Top + Math.Max(0, (iconBounds.Height - imageSize.Height) / 2);

        imageList.Draw(graphics, x, y, imageIndex);
    }

    private Color GetListSubItemTextColor(ListView listView, ListViewItem item, ListViewItem.ListViewSubItem subItem)
    {
        if (IsCutGhostedPath(GetListItemPath(item)))
            return ShellTheme.ItemCutText;

        if (item.Selected)
            return ShellTheme.ItemSelectedText;

        if (!subItem.ForeColor.IsEmpty && subItem.ForeColor != listView.ForeColor)
            return subItem.ForeColor;

        if (!item.ForeColor.IsEmpty)
            return item.ForeColor;

        return listView.ForeColor;
    }

    private static string? GetListItemPath(ListViewItem item)
    {
        return item.Tag is ExplorerListRow row ? row.FullPath : null;
    }

    private static bool IsListSelectionActive(ListView listView)
    {
        return listView.Focused || listView.ContainsFocus;
    }

    private void LvItems_FocusChanged(object? sender, EventArgs e)
    {
        InvalidateSelectedListItems();
    }

    private void InvalidateSelectedListItems()
    {
        foreach (ListViewItem item in _lvItems.SelectedItems)
            InvalidateListItemRow(item);
    }

    private void LvItems_MouseMove(object? sender, MouseEventArgs e)
    {
        ListViewHitTestInfo hit = _lvItems.HitTest(e.Location);
        SetListHoverItem(hit.Item);
    }

    private void LvItems_MouseLeave(object? sender, EventArgs e)
    {
        SetListHoverItem(null);
    }

    private void SetListHoverItem(ListViewItem? item)
    {
        if (ReferenceEquals(_listHoverItem, item))
            return;

        ListViewItem? oldItem = _listHoverItem;
        _listHoverItem = item;

        InvalidateListItemRow(oldItem);
        InvalidateListItemRow(_listHoverItem);
    }

    private void InvalidateListItemRowByIndex(int index)
    {
        if (index< 0 || index >= _lvItems.Items.Count)
            return;

        InvalidateListItemRow(_lvItems.Items[index]);
    }


    private void InvalidateListItemRow(ListViewItem? item)
    {
        if (item == null || _lvItems.IsDisposed || !_lvItems.IsHandleCreated)
            return;

        Rectangle bounds;

        try
        {
            bounds = item.GetBounds(ItemBoundsPortion.Entire);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (bounds.Height <= 0)
            return;

        bounds = new Rectangle(
        0,
        bounds.Top,
        _lvItems.ClientSize.Width,
        bounds.Height);

        _lvItems.Invalidate(bounds, false);
    }

    private void LvItems_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_sortColumn == e.Column)
        {
            _sortOrder = _sortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
        }
        else
        {
            _sortColumn = e.Column;
            _sortOrder = SortOrder.Ascending;
        }

        RenderRows();
    }

    private void ShowListItemContextMenu(ListViewItem item, Point screenLocation)
    {
        if (item.Tag is not ExplorerListRow row)
            return;

        ExplorerCommandContext context = _presenter.CreateListItemCommandContext(
            row,
            GetSelectedTransferablePaths());

        ShowContextMenu(
            _presenter.BuildContextMenu(context),
            context,
            screenLocation,
            ExecuteRenameSelectedListItemCommand);
    }

    private void ShowListBackgroundContextMenu(Point screenLocation)
    {
        ExplorerCommandContext context = _presenter.CreateBackgroundCommandContext();

        ShowContextMenu(
            _presenter.BuildContextMenu(context),
            context,
            screenLocation,
            static () => { });
    }

    private void ShowTreeContextMenu(ExplorerTreeNodeTag tag, Point screenLocation)
    {
        ExplorerCommandContext context = _presenter.CreateTreeNodeCommandContext(tag);

        ShowContextMenu(
            _presenter.BuildContextMenu(context),
            context,
            screenLocation,
            ExecuteRenameSelectedTreeNodeCommand);
    }

    private void ShowContextMenu(
        IReadOnlyList<ExplorerMenuItemModel> items,
        ExplorerCommandContext context,
        Point screenLocation,
        Action beginInlineRename)
    {
        ContextMenuStrip menu = new();
        AppendMenuItems(menu.Items, items, context, beginInlineRename);

        if (menu.Items.Count == 0)
        {
            menu.Dispose();
            return;
        }

        menu.Closed += (_, _) =>
        {
            if (!TryBeginInvoke(menu.Dispose))
                menu.Dispose();
        };
        menu.Show(screenLocation);
    }

    private void AppendMenuItems(
        ToolStripItemCollection items,
        IReadOnlyList<ExplorerMenuItemModel> models,
        ExplorerCommandContext context,
        Action beginInlineRename)
    {
        bool pendingSeparator = false;

        foreach (ExplorerMenuItemModel model in models)
        {
            if (!model.Visible)
                continue;

            if (model.IsSeparator)
            {
                if (items.Count > 0)
                    pendingSeparator = true;

                continue;
            }

            ToolStripItem? item = CreateMenuItem(model, context, beginInlineRename);
            if (item == null)
                continue;

            if (pendingSeparator && items.Count > 0)
            {
                items.Add(new ToolStripSeparator());
                pendingSeparator = false;
            }

            items.Add(item);
        }
    }

    private ToolStripMenuItem? CreateMenuItem(
        ExplorerMenuItemModel model,
        ExplorerCommandContext context,
        Action beginInlineRename)
    {
        if (model.IsSeparator || !model.Visible)
            return null;

        if (model.Children.Count > 0)
        {
            ToolStripMenuItem submenu = new(model.Text)
            {
                Enabled = model.Enabled
            };

            AppendMenuItems(submenu.DropDownItems, model.Children, context, beginInlineRename);
            if (submenu.DropDownItems.Count != 0)
                return submenu;

            submenu.Dispose();
            return null;
        }

        if (model.CommandId is null)
            return null;

        ToolStripMenuItem item = new(model.Text)
        {
            Enabled = model.Enabled
        };

        ExplorerCommandId commandId = model.CommandId.Value;
        string? commandArgument = model.CommandArgument;

        item.Click += (_, _) => ExecuteContextMenuCommand(
            commandId,
            commandArgument,
            context,
            beginInlineRename);
        return item;
    }

    private void ExecuteContextMenuCommand(
    ExplorerCommandId commandId,
    string? commandArgument,
    ExplorerCommandContext context,
    Action beginInlineRename)
    {
        if (commandId == ExplorerCommandId.Open && TryHandlePickerOpenContextCommand(context))
            return;

        bool handled = _presenter.ExecuteContextCommand(commandId, commandArgument, context, beginInlineRename);
        ApplyClipboardVisualStateForCommand(commandId, context, handled);
    }

    private void ApplyClipboardVisualStateForCommand(
        ExplorerCommandId commandId,
        ExplorerCommandContext context,
        bool handled)
    {
        if (!handled)
            return;

        switch (commandId)
        {
            case ExplorerCommandId.Cut:
                SetCutGhostedPaths(GetContextTransferCutPaths(context));
                break;

            case ExplorerCommandId.Copy:
            case ExplorerCommandId.CopyAsPath:
            case ExplorerCommandId.Paste:
                ClearCutGhostedPaths();
                break;
        }
    }

    private static IReadOnlyList<string> GetContextTransferPaths(ExplorerCommandContext context)
    {
        if (context.SelectionPaths.Count > 0)
            return context.SelectionPaths;

        return string.IsNullOrWhiteSpace(context.TargetPath)
            ? Array.Empty<string>()
            : [context.TargetPath];
    }

    private IReadOnlyList<(string Path, bool IsTreePath)> GetContextTransferCutPaths(ExplorerCommandContext context)
    {
        IReadOnlyList<string> paths = GetContextTransferPaths(context);
        if (paths.Count == 0)
            return Array.Empty<(string Path, bool IsTreePath)>();

        List<(string Path, bool IsTreePath)> cutPaths = new(paths.Count);

        foreach (string path in paths)
        {
            bool isTreePath = context.SelectionPaths.Count > 0
                ? IsCurrentDirectoryRowPath(path)
                : IsTreeGhostableTargetKind(context.TargetKind);

            cutPaths.Add((path, isTreePath));
        }

        return cutPaths;
    }

    private bool IsCurrentDirectoryRowPath(string path)
    {
        string? normalizedPath = NormalizeGhostedPath(path);
        if (normalizedPath == null)
            return false;

        foreach (ExplorerListRow row in _currentRows)
        {
            if (row.Kind != ExplorerListRowKind.Directory)
                continue;

            string? rowPath = NormalizeGhostedPath(row.FullPath);
            if (rowPath != null && _cutGhostedPaths.Comparer.Equals(rowPath, normalizedPath))
                return true;
        }

        return false;
    }

    private static bool IsTreeGhostableTargetKind(ExplorerCommandTargetKind targetKind)
    {
        return targetKind is ExplorerCommandTargetKind.Folder or ExplorerCommandTargetKind.Drive;
    }

    private void SetCutGhostedPaths(IEnumerable<string> paths)
    {
        SetCutGhostedPaths(paths.Select(path => (path, IsCurrentDirectoryRowPath(path))));
    }

    private void SetCutGhostedPaths(IEnumerable<(string Path, bool IsTreePath)> paths)
    {
        string[] previousTreePaths = _cutGhostedTreePaths.ToArray();


        _cutGhostedPaths.Clear();
        _cutGhostedTreePaths.Clear();
        _cutGhostedTreeParentPaths.Clear();

        foreach ((string path, bool isTreePath) in paths)
        {
            string? normalizedPath = NormalizeGhostedPath(path);
            if (normalizedPath == null)
                continue;

            _cutGhostedPaths.Add(normalizedPath);

            if (!isTreePath)
                continue;

            _cutGhostedTreePaths.Add(normalizedPath);

            string? parentPath = GetCutGhostedTreeParentPath(normalizedPath);
            if (parentPath != null)
                _cutGhostedTreeParentPaths.Add(parentPath);
        }

        InvalidateCutGhostedItems(previousTreePaths);
    }

    private void ClearCutGhostedPaths()
    {
        if (_cutGhostedPaths.Count == 0)
            return;

        string[] previousTreePaths = _cutGhostedTreePaths.ToArray();

        _cutGhostedPaths.Clear();
        _cutGhostedTreePaths.Clear();
        _cutGhostedTreeParentPaths.Clear();
        
        InvalidateCutGhostedItems(previousTreePaths);
    }

    private bool IsCutGhostedPath(string? path)
    {
        string? normalizedPath = NormalizeGhostedPath(path);
        return normalizedPath != null && _cutGhostedPaths.Contains(normalizedPath);
    }

    private bool IsCutGhostedTreePath(string? path)
    {
        string? normalizedPath = NormalizeGhostedPath(path);
        return normalizedPath != null && _cutGhostedTreePaths.Contains(normalizedPath);
    }

    private bool HasCutGhostedTreeChildrenInFolder(string? folderPath)
    {
        string? normalizedPath = NormalizeGhostedPath(folderPath);
        return normalizedPath != null && _cutGhostedTreeParentPaths.Contains(normalizedPath);
    }

    private static string? NormalizeGhostedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string? GetCutGhostedTreeParentPath(string path)
    {
        try
        {
            DirectoryInfo? parent = Directory.GetParent(path);
            return parent?.FullName == null
                ? null
                : NormalizeGhostedPath(parent.FullName);
        }
        catch
        {
            string? parentPath = Path.GetDirectoryName(path);
            return NormalizeGhostedPath(parentPath);
        }
    }

    private void InvalidateCutGhostedItems(IEnumerable<string>? previousTreePaths = null)
    {
        ApplyCutGhostedTreeIconState(previousTreePaths ?? Array.Empty<string>());

        if (!_lvItems.IsDisposed && _lvItems.IsHandleCreated)
            _lvItems.Invalidate();
    }

    private Point GetListKeyboardMenuLocation(ListViewItem item)
    {
        Rectangle bounds = item.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return GetListBackgroundKeyboardMenuLocation();

        Point clientPoint = new(bounds.Left + Math.Max(8, bounds.Width / 2), bounds.Top + Math.Max(8, bounds.Height / 2));
        return _lvItems.PointToScreen(clientPoint);
    }

    private Point GetListBackgroundKeyboardMenuLocation()
    {
        Rectangle rect = _lvItems.ClientRectangle;
        Point clientPoint = new(Math.Max(8, rect.Width / 2), Math.Max(8, rect.Height / 2));
        return _lvItems.PointToScreen(clientPoint);
    }

    private Point GetTreeKeyboardMenuLocation(TreeNode node)
    {
        Rectangle bounds = node.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return _tvNav.PointToScreen(new Point(8, 8));

        Point clientPoint = new(bounds.Left + Math.Max(8, bounds.Width / 2), bounds.Top + Math.Max(8, bounds.Height / 2));
        return _tvNav.PointToScreen(clientPoint);
    }

    private static string FormatYesNo(bool? value)
    {
        return value switch
        {
            true => "Yes",
            false => "No",
            _ => string.Empty
        };
    }

    private static string FormatDriveCapacity(long? value)
    {
        return FormatCapacity(value);
    }

    private static string FormatSize(ExplorerListRow row)
    {
        if (row.Kind != ExplorerListRowKind.File || !row.SizeBytes.HasValue)
            return string.Empty;

        long sizeKb = Math.Max(1L, (row.SizeBytes.Value + 1023L) / 1024L);
        return $"{sizeKb:N0} KB";
    }

    private static string FormatCapacity(long? value)
    {
        if (!value.HasValue)
            return string.Empty;

        const long oneMb = 1024L * 1024L;
        const long oneGb = 1024L * 1024L * 1024L;
        const long oneTb = 1024L * 1024L * 1024L * 1024L;

        if (value.Value < oneMb)
            return $"{Math.Max(1L, (value.Value + 1023L) / 1024L):N0} KB";

        if (value.Value < oneGb)
            return $"{value.Value / 1024d / 1024d:0.##} MB";

        if (value.Value < oneTb)
            return $"{value.Value / 1024d / 1024d / 1024d:0.##} GB";

        return $"{value.Value / 1024d / 1024d / 1024d / 1024d:0.##} TB";
    }

    private static string FormatDateModified(DateTime? dateModified)
    {
        if (!dateModified.HasValue)
            return string.Empty;

        return dateModified.Value.ToString("M/d/yyyy h:mm tt", CultureInfo.CurrentCulture);
    }

    private static int GetContainerSortBucket(ExplorerListRow row)
    {
        return row.Kind switch
        {
            ExplorerListRowKind.Drive => 0,
            ExplorerListRowKind.Directory => 0,
            _ => 1
        };
    }

    private static string GetNameSortKey(ExplorerListRow row)
    {
        return row.DisplayName ?? string.Empty;
    }

    private static string GetDriveSortKey(ExplorerListRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.FullPath))
        {
            string root = Path.GetPathRoot(row.FullPath) ?? row.FullPath;
            return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return row.DisplayName ?? string.Empty;
    }

    private void AttachThemedListHeader()
    {
        if (_lvItems.IsDisposed || !_lvItems.IsHandleCreated)
            return;

        IntPtr headerHandle = User32.SendMessage(
            _lvItems.Handle,
            User32.LVM_GETHEADER,
            IntPtr.Zero,
            IntPtr.Zero);

        if (headerHandle == IntPtr.Zero)
            return;

        if (_listViewHeaderWatcher?.Handle == headerHandle)
            return;

        _listViewHeaderWatcher?.ReleaseHandle();
        _listViewHeaderWatcher = new ThemedListViewHeaderWatcher(
            headerHandle,
            _lvItems,
            GetListHeaderColumnWidth);
    }

    private void InvalidateThemedListHeader()
    {
        AttachThemedListHeader();
        _listViewHeaderWatcher?.Invalidate();
    }

    private int GetListHeaderColumnWidth()
    {
        int width = 0;

        foreach (ColumnHeader column in _lvItems.Columns)
            width += column.Width;

        return width;
    }


    private void ReleaseListResourcesForClose()
    {
        _listIconRefineVersion++;
        _listIconRefineQueued = false;

        _currentRows = Array.Empty<ExplorerListRow>();

        if (!_lvItems.IsDisposed)
        {
            _lvItems.Items.Clear();
            ClearPathSpecificListImages();
        }

        _listViewViewportWatcher?.ReleaseHandle();
        _listViewViewportWatcher = null;

        _listViewHeaderWatcher?.ReleaseHandle();
        _listViewHeaderWatcher = null;
    }

    private sealed class ThemedListViewHeaderWatcher : NativeWindow
    {
        private readonly ListView _listView;
        private readonly Func<int> _columnWidthProvider;

        public ThemedListViewHeaderWatcher(
            IntPtr headerHandle,
            ListView listView,
            Func<int> columnWidthProvider)
        {
            _listView = listView;
            _columnWidthProvider = columnWidthProvider;
            AssignHandle(headerHandle);
        }

        public void Invalidate()
        {
            if (Handle != IntPtr.Zero)
                User32.InvalidateRect(Handle, IntPtr.Zero, false);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg is User32.WM_PAINT or User32.WM_NCPAINT)
                PaintRemainder();
        }

        private void PaintRemainder()
        {
            if (_listView.IsDisposed || Handle == IntPtr.Zero)
                return;

            if (!User32.GetClientRect(Handle, out User32.RECT rect))
                return;

            int width = Math.Max(0, rect.Right - rect.Left);
            int height = Math.Max(0, rect.Bottom - rect.Top);
            if (width == 0 || height == 0)
                return;

            int columnsRight = Math.Min(width, Math.Max(0, _columnWidthProvider()));
            if (columnsRight >= width)
                return;

            Rectangle remainderBounds = new(columnsRight, 0, width - columnsRight, height);

            using Graphics graphics = Graphics.FromHwnd(Handle);
            using SolidBrush backBrush = new(ShellTheme.ContentBack);
            graphics.FillRectangle(backBrush, remainderBounds);

            using Pen separatorPen = new(ShellTheme.ContentBorder);

            // Bottom header edge across the painted spacer.
            graphics.DrawLine(
                separatorPen,
                remainderBounds.Left,
                remainderBounds.Bottom - 1,
                remainderBounds.Right,
                remainderBounds.Bottom - 1);

            // Visible divider/grab edge between the last real header and the spacer.
            // The native resize hit-test is already there; this just restores the visual cue.
            if (columnsRight > 0 && height > 4)
            {
                graphics.DrawLine(
                    separatorPen,
                    remainderBounds.Left,
                    remainderBounds.Top + 2,
                    remainderBounds.Left,
                    remainderBounds.Bottom - 3);
            }
        }

    }

    private sealed class ListViewViewportWatcher : NativeWindow
    {
        private const int WM_VSCROLL = 0x0115;
        private const int WM_HSCROLL = 0x0114;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_KEYUP = 0x0101;

        private readonly Control _control;
        private readonly Action _viewportChanged;

        public ListViewViewportWatcher(Control control, Action viewportChanged)
        {
            _control = control;
            _viewportChanged = viewportChanged;

            if (control.IsHandleCreated)
                AssignHandle(control.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg is WM_VSCROLL or WM_HSCROLL or WM_MOUSEWHEEL or WM_KEYUP)
            {
                if (!_control.IsDisposed && _control.IsHandleCreated)
                    _viewportChanged();
            }
        }
    }

    private sealed class ExplorerListRowComparer : IComparer<ExplorerListRow>
    {
        private readonly bool _isShowingDriveRows;
        private readonly int _sortColumn;
        private readonly SortOrder _sortOrder;

        public ExplorerListRowComparer(bool isShowingDriveRows, int sortColumn, SortOrder sortOrder)
        {
            _isShowingDriveRows = isShowingDriveRows;
            _sortColumn = sortColumn;
            _sortOrder = sortOrder;
        }

        public int Compare(ExplorerListRow? left, ExplorerListRow? right)
        {
            if (ReferenceEquals(left, right))
                return 0;

            if (left is null)
                return -1;

            if (right is null)
                return 1;

            int bucketCompare = GetContainerSortBucket(left).CompareTo(GetContainerSortBucket(right));
            if (bucketCompare != 0)
                return bucketCompare;

            int primaryCompare = _isShowingDriveRows
                ? CompareDriveRows(left, right)
                : CompareDirectoryRows(left, right);

            if (primaryCompare != 0)
                return _sortOrder == SortOrder.Descending ? -primaryCompare : primaryCompare;

            return StringComparer.OrdinalIgnoreCase.Compare(
                GetNameSortKey(left),
                GetNameSortKey(right));
        }

        private int CompareDriveRows(ExplorerListRow left, ExplorerListRow right)
        {
            return _sortColumn switch
            {
                1 => StringComparer.OrdinalIgnoreCase.Compare(left.DriveType?.ToString(), right.DriveType?.ToString()),
                2 => CompareNullable(left.IsReady, right.IsReady),
                3 => CompareNullable(left.IsLocked, right.IsLocked),
                4 => CompareNullable(left.FreeSpaceBytes, right.FreeSpaceBytes),
                5 => CompareNullable(left.TotalSizeBytes, right.TotalSizeBytes),
                _ => StringComparer.OrdinalIgnoreCase.Compare(GetDriveSortKey(left), GetDriveSortKey(right))
            };
        }

        private int CompareDirectoryRows(ExplorerListRow left, ExplorerListRow right)
        {
            return _sortColumn switch
            {
                1 => CompareNullable(left.ModifiedLocalTime, right.ModifiedLocalTime),
                2 => StringComparer.OrdinalIgnoreCase.Compare(left.TypeText, right.TypeText),
                3 => CompareNullable(left.SizeBytes, right.SizeBytes),
                _ => StringComparer.OrdinalIgnoreCase.Compare(GetNameSortKey(left), GetNameSortKey(right))
            };
        }

        private static int CompareNullable<T>(T? left, T? right)
            where T : struct, IComparable<T>
        {
            if (left.HasValue && right.HasValue)
                return left.Value.CompareTo(right.Value);

            if (left.HasValue)
                return 1;

            if (right.HasValue)
                return -1;

            return 0;
        }
    }
}