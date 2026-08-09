using Shared.Shell.Interop;
using System.Runtime.InteropServices;

namespace Explorer.UI.Shell;

public partial class ExplorerShellWindow
{
    private const int LVM_GETEDITCONTROL = 0x1018;
    private const int TVM_GETEDITCONTROL = 0x110F;
    private const int EM_SETSEL = 0x00B1;
    private bool _allowTreeLabelEdit;
    private const int InlineRenameRightPaddingDip = 24;
    private const uint SwpNoZOrder = 0x0004;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    internal bool SelectListItemByPathAndBeginRename(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || _lvItems.IsDisposed)
            return false;

        foreach (ListViewItem item in _lvItems.Items)
        {
            if (item.Tag is not ExplorerListRow row ||
                !PathsEqualForListRename(row.FullPath, path))
            {
                continue;
            }

            _lvItems.SelectedItems.Clear();

            item.Selected = true;
            item.Focused = true;
            _lvItems.FocusedItem = item;
            item.EnsureVisible();
            _lvItems.Focus();

            item.BeginEdit();
            return true;
        }

        return false;
    }

    private static bool PathsEqualForListRename(string? left, string? right)
    {
        if (left == null || right == null)
            return left == right;

        return string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private void BeginRenameSelectedListItem()
    {
        if (_lvItems.SelectedItems.Count != 1)
            return;

        ListViewItem item = _lvItems.SelectedItems[0];

        if (item.Tag is not ExplorerListRow row || !IsRenameSupportedRow(row))
            return;

        _lvItems.Focus();
        item.Focused = true;
        _lvItems.FocusedItem = item;
        item.BeginEdit();
    }

    private void BeginRenameSelectedTreeNode()
    {
        TreeNode? node = _tvNav.SelectedNode;
        if (node?.Tag is not ExplorerTreeNodeTag tag || !IsRenameSupportedTreeTag(tag))
            return;

        _allowTreeLabelEdit = true;

        try
        {
            _tvNav.Focus();
            node.BeginEdit();
        }
        finally
        {
            if (!TryBeginInvoke(() => _allowTreeLabelEdit = false))
                _allowTreeLabelEdit = false;
        }

    }

    private void LvItems_BeforeLabelEdit(object? sender, LabelEditEventArgs e)
    {
        if (e.Item < 0 || e.Item >= _lvItems.Items.Count)
        {
            e.CancelEdit = true;
            return;
        }

        ListViewItem item = _lvItems.Items[e.Item];

        if (item.Tag is not ExplorerListRow row || !IsRenameSupportedRow(row))
        {
            e.CancelEdit = true;
            return;
        }

        if (_lvItems.SelectedItems.Count != 1)
        {
            e.CancelEdit = true;
            return;
        }

        if (row.Kind == ExplorerListRowKind.Drive)
        {
            TryBeginInvoke(() =>
            {
                string currentLabel = GetCurrentDriveLabel(row.FullPath);
                PrepareListEditText(currentLabel);
            });
        }
        else
        {
            TryBeginInvoke(() => PrepareListEditText(row.DisplayName));
        }
    }

    private void LvItems_AfterLabelEdit(object? sender, LabelEditEventArgs e)
    {
        if (e.Item < 0 || e.Item >= _lvItems.Items.Count)
            return;

        if (e.Label == null)
            return;

        e.CancelEdit = true;

        ListViewItem item = _lvItems.Items[e.Item];

        if (item.Tag is not ExplorerListRow row)
            return;

        _presenter.CommitListRename(row, e.Label);
    }

    private void TvNav_BeforeLabelEdit(object? sender, NodeLabelEditEventArgs e)
    {
        if (!_allowTreeLabelEdit ||
            e.Node?.Tag is not ExplorerTreeNodeTag tag ||
            !IsRenameSupportedTreeTag(tag) ||
            string.IsNullOrWhiteSpace(tag.Path))
        {
            e.CancelEdit = true;
            return;
        }

        _treeEditingNode = e.Node;

        TryBeginInvoke(() =>
        {
            string currentLabel = GetCurrentTreeLabel(tag);
            PrepareTreeEditText(currentLabel);
        });
    }

    private void TvNav_AfterLabelEdit(object? sender, NodeLabelEditEventArgs e)
    {
        TreeNode? editedNode = _treeEditingNode;
        _treeEditingNode = null;
        InvalidateTreeNode(editedNode);

        if (e.Node?.Tag is not ExplorerTreeNodeTag tag)
            return;

        if (e.Label == null)
            return;

        e.CancelEdit = true;
        _presenter.CommitTreeRename(tag, e.Label);
    }

    private void PrepareListEditText(string editText)
    {
        if (IsDisposed || !_lvItems.IsHandleCreated)
            return;

        IntPtr editHandle = SendMessage(_lvItems.Handle, LVM_GETEDITCONTROL, IntPtr.Zero, IntPtr.Zero);
        if (editHandle == IntPtr.Zero)
            return;

        SetWindowText(editHandle, editText);

        int selectionLength = editText.Length;

        if (!string.IsNullOrEmpty(editText))
        {
            string extension = Path.GetExtension(editText);
            if (!string.IsNullOrEmpty(extension) &&
                !string.Equals(editText, extension, StringComparison.Ordinal))
            {
                selectionLength = editText.Length - extension.Length;
            }
        }

        SendMessage(editHandle, EM_SETSEL, IntPtr.Zero, (IntPtr)selectionLength);
        ResizeInlineEditControl(editHandle, editText, _lvItems, _lvItems.Font);
    }

    private void PrepareTreeEditText(string editText)
    {
        if (IsDisposed || !_tvNav.IsHandleCreated)
            return;

        IntPtr editHandle = SendMessage(_tvNav.Handle, TVM_GETEDITCONTROL, IntPtr.Zero, IntPtr.Zero);
        if (editHandle == IntPtr.Zero)
            return;

        SetWindowText(editHandle, editText);
        SendMessage(editHandle, EM_SETSEL, IntPtr.Zero, (IntPtr)editText.Length);
        ResizeInlineEditControl(
            editHandle,
            editText,
            _tvNav,
            _tvNav.Font,
            verticalBounds: _treeEditingNode?.Bounds);
    }

    private static bool IsRenameSupportedRow(ExplorerListRow row)
    {
        return row.Kind is ExplorerListRowKind.File
            or ExplorerListRowKind.Directory
            or ExplorerListRowKind.Drive;
    }

    private void ResizeInlineEditControl(
        IntPtr editHandle,
        string editText,
        Control ownerControl,
        Font font,
        Rectangle? verticalBounds = null)
    {
        if (editHandle == IntPtr.Zero)
            return;

        if (!User32.GetWindowRect(editHandle, out User32.RECT rect))
            return;

        int currentHeight = rect.Bottom - rect.Top;
        int currentWidth = rect.Right - rect.Left;

        int desiredWidth =
            TextRenderer.MeasureText(editText, font).Width +
            ScaleDip(InlineRenameRightPaddingDip);

        if (desiredWidth < currentWidth)
            desiredWidth = currentWidth;

        Point currentLocation = ownerControl.PointToClient(new Point(rect.Left, rect.Top));

        int x = currentLocation.X;
        int y = currentLocation.Y;
        int height = currentHeight;
        uint flags = SwpNoZOrder | User32.SWP_NOACTIVATE;

        if (verticalBounds.HasValue && verticalBounds.Value.Height > 0)
        {
            y = verticalBounds.Value.Top;
            height = verticalBounds.Value.Height;
        }
        else
        {
            flags |= User32.SWP_NOMOVE;
        }

        User32.SetWindowPos(
            editHandle,
            IntPtr.Zero,
            x,
            y,
            desiredWidth,
            height,
            flags);
    }

    private static bool IsRenameSupportedTreeTag(ExplorerTreeNodeTag tag)
    {
        return tag.Kind is ExplorerTreeNodeKind.Drive or ExplorerTreeNodeKind.Folder &&
               !string.IsNullOrWhiteSpace(tag.Path);
    }

    private static string GetCurrentTreeLabel(ExplorerTreeNodeTag tag)
    {
        if (string.IsNullOrWhiteSpace(tag.Path))
            return string.Empty;

        if (tag.Kind == ExplorerTreeNodeKind.Drive)
            return GetCurrentDriveLabel(tag.Path);

        try
        {
            return new DirectoryInfo(tag.Path).Name;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetCurrentDriveLabel(string rootPath)
    {
        try
        {
            DriveInfo drive = new(rootPath);
            if (drive.IsReady)
                return drive.VolumeLabel ?? string.Empty;
        }
        catch
        {
        }

        return string.Empty;
    }
}