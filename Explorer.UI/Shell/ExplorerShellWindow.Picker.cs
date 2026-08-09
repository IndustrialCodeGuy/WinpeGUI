using Shell.Core.Models;

namespace Explorer.UI.Shell;

public partial class ExplorerShellWindow
{
    private static string[] BuildAllowedExtensionDisplayList(IEnumerable<string>? allowedExtensions)
    {
        if (allowedExtensions == null)
            return Array.Empty<string>();

        List<string> results = new();

        foreach (string? extension in allowedExtensions)
        {
            string normalized = NormalizeAllowedExtension(extension);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            if (!results.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                results.Add(normalized);
        }

        return results.ToArray();
    }

    private static string NormalizeAllowedExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        string normalized = extension.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "*" || normalized == "*.*")
            return string.Empty;

        if (normalized.StartsWith("*", StringComparison.Ordinal))
            normalized = normalized[1..].TrimStart();

        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (!normalized.StartsWith(".", StringComparison.Ordinal))
            normalized = "." + normalized;

        return normalized;
    }

    private bool IsPickerMode => _mode != ExplorerWindowMode.Browse;

    private string? GetActiveExtensionFilter()
    {
        if (_mode is not (ExplorerWindowMode.OpenFile or ExplorerWindowMode.SaveFile))
            return null;

        if (_cmbFileType.SelectedIndex < 0 || _cmbFileType.SelectedIndex >= _allowedExtensionsDisplay.Length)
            return null;

        return _allowedExtensionsDisplay[_cmbFileType.SelectedIndex];
    }

    private bool IsAllowedByCurrentFilter(string path)
    {
        string? activeFilter = GetActiveExtensionFilter();
        if (string.IsNullOrWhiteSpace(activeFilter))
            return true;

        return string.Equals(Path.GetExtension(path), activeFilter, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAllowedByCurrentFilter(ExplorerListRow row)
    {
        if (row.Kind != ExplorerListRowKind.File)
            return true;

        string? activeFilter = GetActiveExtensionFilter();
        if (string.IsNullOrWhiteSpace(activeFilter))
            return true;

        return string.Equals(row.Extension, activeFilter, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveSaveFileName(string rawFileName)
    {
        string fileName = (rawFileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        string? activeFilter = GetActiveExtensionFilter();
        if (string.IsNullOrWhiteSpace(activeFilter))
            return fileName;

        string typedExtension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(typedExtension))
            return fileName + activeFilter;

        if (string.Equals(typedExtension, activeFilter, StringComparison.OrdinalIgnoreCase))
            return fileName;

        return fileName + activeFilter;
    }

    private string? GetSelectedFilePathForPicker()
    {
        ExplorerListRow? row = GetSelectedRow();
        if (row?.Kind != ExplorerListRowKind.File)
            return null;

        return row.FullPath;
    }

    private string? GetSelectedFolderPathForPicker()
    {
        ExplorerListRow? row = GetSelectedRow();
        if (row?.Kind is not (ExplorerListRowKind.Directory or ExplorerListRowKind.Drive))
            return null;

        return Directory.Exists(row.FullPath)
            ? row.FullPath
            : null;
    }

    private void UpdatePickerFileNameFromSelection()
    {
        if (_mode is not (ExplorerWindowMode.OpenFile or ExplorerWindowMode.SaveFile) || !_txtFileName.Visible)
            return;

        ExplorerListRow? row = GetSelectedRow();
        _txtFileName.Text = row?.Kind == ExplorerListRowKind.File
            ? row.DisplayName
            : string.Empty;
    }

    private void AcceptPickerPath(string path)
    {
        SelectedPath = path;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CancelPicker()
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void ExecutePickerOkCommand()
    {
        switch (_mode)
        {
            case ExplorerWindowMode.OpenFile:
                ExecuteOpenFilePickerOkCommand();
                return;

            case ExplorerWindowMode.SelectFolder:
                ExecuteSelectFolderPickerOkCommand();
                return;

            case ExplorerWindowMode.SaveFile:
                ExecuteSaveFilePickerOkCommand();
                return;
        }
    }

    private void ExecuteOpenFilePickerOkCommand()
    {
        string fileName = _txtFileName.Text.Trim();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            string? selectedPath = GetSelectedFilePathForPicker();
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                MessageBox.Show(this, "Select a file or enter a file name.", "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            TryAcceptOpenFilePathForPicker(selectedPath);
            return;
        }

        string? currentPath = _presenter.CurrentFileSystemPath;
        string candidatePath = Path.IsPathRooted(fileName)
            ? fileName
            : Path.Combine(currentPath ?? string.Empty, fileName);

        if (!File.Exists(candidatePath))
        {
            MessageBox.Show(this, "The specified file was not found.", "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        TryAcceptOpenFilePathForPicker(candidatePath);
    }

    private void ExecuteSelectFolderPickerOkCommand()
    {
        string? selectedPath = GetSelectedFolderPathForPicker();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            AcceptPickerPath(selectedPath);
            return;
        }

        string? currentPath = _presenter.CurrentFileSystemPath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            MessageBox.Show(this, "Select a folder first.", "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AcceptPickerPath(currentPath);
    }

    private void ExecuteSaveFilePickerOkCommand()
    {
        string? currentPath = _presenter.CurrentFileSystemPath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            MessageBox.Show(this, "Select a folder first.", "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string fileName = ResolveSaveFileName(_txtFileName.Text);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            MessageBox.Show(this, "Enter a file name.", "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            AcceptPickerPath(Path.Combine(currentPath, fileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Invalid file name.\n\n{ex.Message}", "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private bool TryAcceptOpenFilePathForPicker(string? path)
    {
        if (_mode != ExplorerWindowMode.OpenFile || string.IsNullOrWhiteSpace(path))
            return false;

        if (!File.Exists(path))
            return false;

        if (!IsAllowedByCurrentFilter(path))
        {
            _txtFileName.Text = Path.GetFileName(path);
            _txtFileName.SelectAll();
            _txtFileName.Focus();
            MessageBox.Show(this, "That file type is not allowed by the selected filter.", "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        AcceptPickerPath(path);
        return true;
    }

    private bool TryHandlePickerActivatedRow(ExplorerListRow? row)
    {
        if (row == null)
            return false;

        if (_mode == ExplorerWindowMode.OpenFile && row.Kind == ExplorerListRowKind.File)
            return TryAcceptOpenFilePathForPicker(row.FullPath);

        if (_mode == ExplorerWindowMode.SaveFile && row.Kind == ExplorerListRowKind.File)
        {
            _txtFileName.Text = row.DisplayName;
            _txtFileName.SelectAll();
            _txtFileName.Focus();
            return true;
        }

        return false;
    }

    private bool TryHandlePickerOpenContextCommand(ExplorerCommandContext context)
    {
        if (context.TargetKind != ExplorerCommandTargetKind.File)
            return false;

        if (_mode == ExplorerWindowMode.OpenFile)
            return TryAcceptOpenFilePathForPicker(context.TargetPath);

        if (_mode == ExplorerWindowMode.SaveFile && !string.IsNullOrWhiteSpace(context.TargetPath))
        {
            _txtFileName.Text = Path.GetFileName(context.TargetPath);
            _txtFileName.SelectAll();
            _txtFileName.Focus();
            return true;
        }

        return false;
    }
}
