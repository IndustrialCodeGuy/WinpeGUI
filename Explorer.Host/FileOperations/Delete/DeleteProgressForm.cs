using Shared.Shell.Utilities;
using Shell.Core.FileTypes;
using System.Globalization;
using UiExplorerIconCache = Explorer.UI.Icons.ExplorerIconCache;

namespace Explorer.Host.FileOperations.Delete;

internal sealed class DeleteProgressForm : Form, IExplorerDeleteProgressSink
{
    private enum DeleteWindowMode
    {
        Progress,
        Error,
        ConfirmDelete
    }

    private enum ConfirmDeleteItemKind
    {
        File,
        Folder,
        Shortcut,
        Items
    }

    private readonly DeleteLayoutMetrics _mDip = new();
    private DeleteLayoutMetricsPx _mPx = new();

    private readonly IExplorerFileAssociationService _fileAssociations;
    private readonly Action<string>? _openFolderInNewWindow;

    private readonly Panel _summaryPanel;
    private readonly Label _lblSummaryPrefix;
    private readonly LinkLabel _lnkSourceFolder;
    private readonly Label _lblOperation;
    private readonly Label _lblStatus;
    private readonly Label _lblSource;
    private readonly Label _lblDetail;
    private readonly ProgressBar _progressBar;
    private readonly Label _lblCurrentName;
    private readonly Label _lblItemsRemaining;
    private readonly CheckBox _chkDoThisForAll;
    private readonly PictureBox _picConfirmIcon;
    private readonly Label _lblConfirmName;
    private readonly Label _lblConfirmType;
    private readonly Label _lblConfirmSize;
    private readonly Label _lblConfirmModified;
    private readonly Button _btnPrimary;
    private readonly Button _btnSecondary;
    private readonly Button _btnCancel;
    private readonly ToolTip _pathToolTip;

    private const int SummaryTextTopMarginDip = 3;
    private const int SummaryLinkTopMarginDip = 4;
    private const int SummaryInlineGapDip = -3;

    private const string WindowIconLibraryPath = @"%SystemRoot%\System32\shell32.dll";
    private const int DeleteWindowIconIndex = 271;
    private const int FolderWindowIconIndex = 234;

    private const int DefaultDpi = 96;

    private Font? _bodyFont;
    private Font? _headerFont;
    private float _lastBodyFontSizePx;
    private float _lastHeaderFontSizePx;
    private int _currentDpi = DefaultDpi;
    private Icon? _windowIcon;

    private bool _isCancelled;
    private bool _errorCanApplyToAllItems;
    private bool _errorIsSingleFileDelete;
    private ExplorerDeleteErrorAction? _applyToAllErrorAction;
    private DeleteWindowMode _mode = DeleteWindowMode.Progress;
    private TaskCompletionSource<ExplorerDeleteErrorAction>? _pendingErrorAction;
    private Action? _pendingDeleteStart;

    private string _lastOperation = "Preparing...";
    private string _lastSourcePath = string.Empty;
    private string _summarySourceFolderPath = string.Empty;
    private string _summarySourceFolderText = "Unknown";
    private long _totalBytes;
    private long _completedBytes;
    private long _totalItemCount;
    private long _completedItemCount;
    private bool _progressComplete;

    private string _confirmIconPath = string.Empty;
    private bool _confirmIconIsDirectory;
    private bool _confirmDetailsActive;
    private bool _confirmDetailsIsDirectory;

    private Form? _ownerForm;

    public bool IsCancelled => _isCancelled;

    public DeleteProgressForm(
        IExplorerFileAssociationService fileAssociations,
        Action<string>? openFolderInNewWindow = null)
    {
        _fileAssociations = fileAssociations ?? throw new ArgumentNullException(nameof(fileAssociations));
        _openFolderInNewWindow = openFolderInNewWindow;

        Text = "Delete";
        ShellDialogChrome.ApplyFixedDialogDefaults(this);
        AutoScaleMode = AutoScaleMode.None;
        CaptureCurrentDpi();

        _pathToolTip = new ToolTip
        {
            AutoPopDelay = 10000,
            InitialDelay = 400,
            ReshowDelay = 100,
            ShowAlways = true
        };

        _summaryPanel = new Panel
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Visible = false
        };

        _lblSummaryPrefix = new Label
        {
            AutoSize = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _lnkSourceFolder = CreatePathLinkLabel();

        _lblOperation = CreateWrapLabel(visible: true);
        _lblOperation.Text = "Preparing...";

        _lblStatus = CreateEllipsisLabel(visible: false);

        _picConfirmIcon = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.CenterImage,
            Visible = false
        };

        _lblConfirmName = CreateEllipsisLabel(visible: false);
        _lblConfirmType = CreateEllipsisLabel(visible: false);
        _lblConfirmSize = CreateEllipsisLabel(visible: false);
        _lblConfirmModified = CreateEllipsisLabel(visible: false);

        _lblSource = CreateEllipsisLabel(visible: true);
        _lblDetail = CreateWrapLabel(visible: true);
        _lblCurrentName = CreateEllipsisLabel(visible: false);
        _lblItemsRemaining = CreateEllipsisLabel(visible: false);

        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        _chkDoThisForAll = new CheckBox
        {
            Text = "Do this for all current items",
            AutoSize = false,
            Visible = false
        };

        _btnPrimary = new Button
        {
            Visible = false
        };
        _btnPrimary.Click += BtnPrimary_Click;

        _btnSecondary = new Button
        {
            Visible = false
        };
        _btnSecondary.Click += BtnSecondary_Click;

        _btnCancel = new Button
        {
            Text = "Cancel"
        };
        _btnCancel.Click += BtnCancel_Click;

        _summaryPanel.Controls.Add(_lblSummaryPrefix);
        _summaryPanel.Controls.Add(_lnkSourceFolder);

        Controls.Add(_summaryPanel);
        Controls.Add(_lblOperation);
        Controls.Add(_lblStatus);
        Controls.Add(_picConfirmIcon);
        Controls.Add(_lblConfirmName);
        Controls.Add(_lblConfirmType);
        Controls.Add(_lblConfirmSize);
        Controls.Add(_lblConfirmModified);
        Controls.Add(_lblSource);
        Controls.Add(_lblDetail);
        Controls.Add(_progressBar);
        Controls.Add(_lblCurrentName);
        Controls.Add(_lblItemsRemaining);
        Controls.Add(_chkDoThisForAll);
        Controls.Add(_btnPrimary);
        Controls.Add(_btnSecondary);
        Controls.Add(_btnCancel);

        AcceptButton = _btnPrimary;
        CancelButton = _btnCancel;

        ReapplyDpiMetrics(updateLayout: false);
        ApplyProgressMode();

        FormClosing += DeleteProgressForm_FormClosing;
    }

    public void ShowDeleteConfirmation(Form? owner, IReadOnlyList<string> paths, Action onConfirmed)
    {
        if (paths == null || paths.Count == 0)
            return;

        RunOnUiThread(() =>
        {
            _ownerForm = owner;
            UpdateSummarySourceFolder(paths);
            _totalItemCount = Math.Max(0, paths.Count);
            ShowDeleteConfirmMode(paths, onConfirmed);
        });
    }

    public void InitializeProgress(long totalBytes, long totalItemCount)
    {
        RunOnUiThread(() =>
        {
            _totalBytes = Math.Max(0, totalBytes);
            _completedBytes = 0;
            _totalItemCount = Math.Max(0, totalItemCount);
            _completedItemCount = 0;
            _progressComplete = false;
            UpdateSummaryLineText();
            UpdateProgressBarValue();
            ApplyLastProgressText();
        });
    }

    public void ReportProgress(string operation, string sourcePath)
    {
        _lastOperation = operation ?? string.Empty;
        _lastSourcePath = sourcePath ?? string.Empty;

        RunOnUiThread(ApplyLastProgressText);
    }

    public void AdjustCompletedBytes(long bytesDelta)
    {
        if (bytesDelta == 0)
            return;

        RunOnUiThread(() =>
        {
            _completedBytes = Math.Max(0, _completedBytes + bytesDelta);
            UpdateProgressBarValue();
            ApplyLastProgressText();
        });
    }

    public void AdjustCompletedItems(long itemsDelta)
    {
        if (itemsDelta == 0)
            return;

        RunOnUiThread(() =>
        {
            _completedItemCount = Math.Max(0, _completedItemCount + itemsDelta);
            ApplyLastProgressText();
        });
    }

    public void CompleteProgress()
    {
        RunOnUiThread(() =>
        {
            _completedBytes = _totalBytes;
            _completedItemCount = _totalItemCount;
            _progressComplete = true;
            UpdateProgressBarValue();
            ApplyLastProgressText();
        });
    }

    public ExplorerDeleteErrorAction HandleError(string sourcePath, Exception exception)
    {
        if (_isCancelled || IsDisposed)
            return ExplorerDeleteErrorAction.Cancel;

        TaskCompletionSource<ExplorerDeleteErrorAction> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        bool invoked = RunOnUiThread(() =>
        {
            if (_isCancelled || IsDisposed)
            {
                tcs.TrySetResult(ExplorerDeleteErrorAction.Cancel);
                return;
            }

            ShowErrorMode(sourcePath, exception, tcs);
        });

        if (!invoked)
            return ExplorerDeleteErrorAction.Cancel;

        return tcs.Task.GetAwaiter().GetResult();
    }

    private void SetWindowIcon(ConfirmDeleteItemKind itemKind)
    {
        Icon? newIcon = LoadWindowIcon(itemKind);
        if (newIcon == null)
            return;

        Icon? oldIcon = _windowIcon;

        _windowIcon = newIcon;
        Icon = newIcon;

        oldIcon?.Dispose();
    }

    private static Icon? LoadWindowIcon(ConfirmDeleteItemKind itemKind)
    {
        string iconPath = Environment.ExpandEnvironmentVariables(WindowIconLibraryPath);
        int iconSize = Math.Max(16, SystemInformation.SmallIconSize.Width);

        int iconIndex = itemKind == ConfirmDeleteItemKind.Folder
            ? FolderWindowIconIndex
            : DeleteWindowIconIndex;

        return IconUtil.FromFileIconIndexIcon(
            iconPath,
            iconIndex,
            iconSize);
    }

    private void ShowDeleteConfirmMode(IReadOnlyList<string> paths, Action onConfirmed)
    {
        _pendingErrorAction = null;
        _pendingDeleteStart = onConfirmed;
        _mode = DeleteWindowMode.ConfirmDelete;

        string firstPath = paths.Count > 0 ? paths[0] ?? string.Empty : string.Empty;

        _lastOperation = "Deleting...";
        _lastSourcePath = firstPath;

        bool singleItem = paths.Count == 1;
        bool isDirectory = singleItem && Directory.Exists(firstPath);

        _applyToAllErrorAction = null;
        _errorCanApplyToAllItems = !singleItem || isDirectory;
        _errorIsSingleFileDelete = singleItem && !isDirectory;

        ConfirmDeleteItemKind itemKind = singleItem
            ? GetConfirmDeleteItemKind(firstPath, isDirectory)
            : ConfirmDeleteItemKind.Items;

        string itemKindText = GetConfirmDeleteItemText(itemKind);

        _lblOperation.Text = singleItem
            ? $"Are you sure you want to permanently delete this {itemKindText}?"
            : $"Are you sure you want to permanently delete these {paths.Count} items?";

        SetWindowIcon(itemKind);

        _lblSource.Visible = !singleItem;
        _lblSource.Text = singleItem ? string.Empty : "These items will be permanently deleted.";

        _lblDetail.Visible = false;
        _lblDetail.Text = string.Empty;

        if (singleItem)
            ApplyConfirmDetails(firstPath, itemKind);
        else
            ClearConfirmDetails();

        _summaryPanel.Visible = false;
        _lblOperation.Visible = true;
        _lblStatus.Visible = false;
        _lblCurrentName.Visible = false;
        _lblItemsRemaining.Visible = false;
        _progressBar.Visible = false;
        _chkDoThisForAll.Visible = false;
        _chkDoThisForAll.Checked = false;

        _btnPrimary.Text = "Yes";
        _btnPrimary.Visible = true;

        _btnSecondary.Visible = false;

        _btnCancel.Text = "No";
        _btnCancel.Visible = true;
        _btnCancel.Enabled = true;
        AcceptButton = _btnPrimary;
        CancelButton = _btnCancel;

        Text = GetConfirmWindowTitle(itemKind);

        ApplyLayoutMetrics();
        EnsureShownCore();
    }

    private void ShowErrorMode(
        string sourcePath,
        Exception exception,
        TaskCompletionSource<ExplorerDeleteErrorAction> tcs)
    {
        if (_applyToAllErrorAction.HasValue)
        {
            tcs.TrySetResult(_applyToAllErrorAction.Value);
            return;
        }

        _pendingErrorAction = tcs;
        _pendingDeleteStart = null;
        _mode = DeleteWindowMode.Error;

        bool isDirectory = Directory.Exists(sourcePath);
        ConfirmDeleteItemKind itemKind = GetConfirmDeleteItemKind(sourcePath, isDirectory);
        ApplyConfirmDetails(sourcePath, itemKind);

        _summaryPanel.Visible = false;
        _lblOperation.Visible = true;
        _lblStatus.Visible = false;
        _lblCurrentName.Visible = false;
        _lblItemsRemaining.Visible = false;

        GetDeleteErrorText(exception, itemKind, out string errorHeader, out string errorDetail);

        _lblOperation.Text = errorHeader;
        _lblSource.Text = string.Empty;
        _lblSource.Visible = false;
        _lblDetail.Text = errorDetail;
        _lblDetail.Visible = true;

        _progressBar.Visible = false;

        _chkDoThisForAll.Checked = false;
        _chkDoThisForAll.Visible = _errorCanApplyToAllItems;

        _btnPrimary.Text = "Try Again";
        _btnPrimary.Visible = true;

        _btnSecondary.Text = "Skip";
        _btnSecondary.Visible = !_errorIsSingleFileDelete;

        _btnCancel.Text = "Cancel";
        _btnCancel.Visible = true;
        _btnCancel.Enabled = true;
        AcceptButton = _btnPrimary;
        CancelButton = _btnCancel;
        SetWindowIcon(itemKind);
        Text = "Delete";

        ApplyLayoutMetrics();
        EnsureShownCore();
    }

    private static void GetDeleteErrorText(
        Exception exception,
        ConfirmDeleteItemKind itemKind,
        out string header,
        out string detail)
    {
        string noun = GetDeleteErrorNoun(itemKind);
        int errorCode = exception.HResult & 0xFFFF;

        switch (errorCode)
        {
            case 2:   // ERROR_FILE_NOT_FOUND
            case 3:   // ERROR_PATH_NOT_FOUND
                header = $"This {noun} is no longer available.";
                detail = "It may have already been moved or deleted.";
                return;

            case 5:   // ERROR_ACCESS_DENIED
                header = $"You need permission to delete this {noun}.";
                detail = "Check the permissions and try again.";
                return;

            case 19:  // ERROR_WRITE_PROTECT
                header = "The action can't be completed because the disk is write-protected.";
                detail = "Remove write protection and try again.";
                return;

            case 21:  // ERROR_NOT_READY
                header = "The action can't be completed because the drive is not ready.";
                detail = "Make sure the drive is connected and try again.";
                return;

            case 32:  // ERROR_SHARING_VIOLATION
            case 33:  // ERROR_LOCK_VIOLATION
                if (itemKind == ConfirmDeleteItemKind.Folder)
                {
                    header = "The action can't be completed because the folder or a file in it is open in another program.";
                    detail = "Close the folder or file and try again.";
                }
                else
                {
                    header = "The action can't be completed because the file is open in another program.";
                    detail = "Close the file and try again.";
                }
                return;

            case 123: // ERROR_INVALID_NAME
                header = $"The {noun} name is not valid.";
                detail = "Rename the item and try again.";
                return;

            case 145: // ERROR_DIR_NOT_EMPTY
                header = "The action can't be completed because the folder is not empty.";
                detail = "Delete the folder contents and try again.";
                return;

            case 206: // ERROR_FILENAME_EXCED_RANGE
                header = $"The {noun} path is too long.";
                detail = "Shorten the name or move it to a location with a shorter path, then try again.";
                return;
        }

        string message = StripQuotedPathsFromErrorMessage(exception.Message);
        header = "The action can't be completed.";
        detail = string.IsNullOrWhiteSpace(message)
            ? "Try again or skip this item."
            : message;
    }

    private static string StripQuotedPathsFromErrorMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        string text = message.Trim();
        int searchStart = 0;

        while (searchStart < text.Length)
        {
            int quoteStart = text.IndexOf('\'', searchStart);
            if (quoteStart < 0)
                break;

            int quoteEnd = text.IndexOf('\'', quoteStart + 1);
            if (quoteEnd < 0)
                break;

            string quoted = text.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
            if (LooksLikePath(quoted.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            {
                text = text[..quoteStart].TrimEnd() + text[(quoteEnd + 1)..];
                searchStart = Math.Max(0, quoteStart - 1);
                continue;
            }

            searchStart = quoteEnd + 1;
        }

        return text.Trim();
    }

    private static bool LooksLikePath(string value)
    {
        return value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar)
            || value.Contains("\\", StringComparison.Ordinal)
            || value.Contains("/", StringComparison.Ordinal)
            || (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/'));
    }

    private static string GetDeleteErrorNoun(ConfirmDeleteItemKind itemKind)
    {
        return itemKind switch
        {
            ConfirmDeleteItemKind.Folder => "folder",
            ConfirmDeleteItemKind.Items => "item",
            _ => "file"
        };
    }

    private void ApplyProgressMode()
    {
        _mode = DeleteWindowMode.Progress;

        ClearConfirmDetails();

        _summaryPanel.Visible = true;
        _lblOperation.Visible = false;
        _lblStatus.Visible = true;
        _lblSource.Visible = false;
        _lblDetail.Visible = false;
        _progressBar.Visible = true;
        _lblCurrentName.Visible = true;
        _lblItemsRemaining.Visible = true;
        _chkDoThisForAll.Visible = false;
        _chkDoThisForAll.Checked = false;
        _btnPrimary.Visible = false;
        _btnSecondary.Visible = false;

        // Active deletes are permanent in WinPE. Cancellation is available
        // by closing the progress window rather than through a visible button.
        _btnCancel.Text = "Cancel";
        _btnCancel.Visible = false;
        _btnCancel.Enabled = false;

        AcceptButton = null;
        CancelButton = null;
        SetWindowIcon(ConfirmDeleteItemKind.File);
        Text = "Deleting";
        ApplyLastProgressText();
        ApplyLayoutMetrics();
    }

    private void ApplyLastProgressText()
    {
        if (_mode != DeleteWindowMode.Progress)
            return;

        UpdateSummaryLineText();

        _lblStatus.Text = $"{GetProgressPercent()}% complete";
        _lblCurrentName.Text = "Name: " + GetCurrentNameText();
        _lblItemsRemaining.Text = "Items Remaining: " + GetItemsRemainingText();

        _pathToolTip.SetToolTip(_lblStatus, string.Empty);
        _pathToolTip.SetToolTip(_lblCurrentName, string.IsNullOrWhiteSpace(_lastSourcePath) ? string.Empty : _lastSourcePath);
        _pathToolTip.SetToolTip(_lblItemsRemaining, string.Empty);
    }

    private void ApplyConfirmDetails(string path, ConfirmDeleteItemKind itemKind)
    {
        bool isDirectory = itemKind == ConfirmDeleteItemKind.Folder;

        _confirmDetailsActive = true;
        _confirmDetailsIsDirectory = isDirectory;
        SetConfirmDetailsVisible(true);

        string displayPath = path?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
        string fileName = string.IsNullOrWhiteSpace(displayPath) ? path ?? string.Empty : Path.GetFileName(displayPath) ?? string.Empty;

        _lblConfirmName.Text = string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        _lblConfirmName.Visible = true;

        if (isDirectory)
        {
            _lblConfirmType.Visible = false;
            _lblConfirmSize.Visible = false;

            _lblConfirmModified.Text = "Date Created: " + FileOperationText.GetDateCreatedText(path);
            _lblConfirmModified.Visible = true;
        }
        else
        {
            _lblConfirmType.Text = "Type: " + GetFileTypeText(path);
            _lblConfirmSize.Text = "Size: " + FileOperationText.GetSizeText(path);
            _lblConfirmModified.Text = "Date Modified: " + FileOperationText.GetDateModifiedText(path);

            _lblConfirmType.Visible = true;
            _lblConfirmSize.Visible = true;
            _lblConfirmModified.Visible = true;
        }

        SetConfirmIcon(path, isDirectory);
    }

    private void SetConfirmIcon(string path, bool isDirectory)
    {
        _confirmIconPath = path ?? string.Empty;
        _confirmIconIsDirectory = isDirectory;

        Image? oldImage = _picConfirmIcon.Image;
        _picConfirmIcon.Image = null;
        oldImage?.Dispose();
        int iconSize = _mPx.ConfirmIconSize;
        Image? image;
        try
        {
            image = UiExplorerIconCache.CreateUncachedFileSystemItemImage(
                _fileAssociations,
                path,
                isDirectory,
                iconSize);
        }
        catch
        {
            image = isDirectory
                ? IconUtil.FromGenericFolder(iconSize)
                : IconUtil.FromGenericFile(iconSize);
        }

        _picConfirmIcon.Image = image;
    }

    private void ClearConfirmDetails()
    {
        SetConfirmDetailsVisible(false);
        ClearConfirmIcon();
    }

    private void ClearConfirmIcon()
    {
        _confirmIconPath = string.Empty;
        _confirmIconIsDirectory = false;
        _confirmDetailsActive = false;
        _confirmDetailsIsDirectory = false;

        Image? oldImage = _picConfirmIcon.Image;
        _picConfirmIcon.Image = null;
        oldImage?.Dispose();
    }

    private void SetConfirmDetailsVisible(bool visible)
    {
        _picConfirmIcon.Visible = visible;
        _lblConfirmName.Visible = visible;

        if (!visible)
        {
            _confirmDetailsActive = false;
            _confirmDetailsIsDirectory = false;
            _lblConfirmType.Visible = false;
            _lblConfirmSize.Visible = false;
            _lblConfirmModified.Visible = false;
        }
    }

    private static ConfirmDeleteItemKind GetConfirmDeleteItemKind(string path, bool isDirectory)
    {
        if (isDirectory)
            return ConfirmDeleteItemKind.Folder;

        return IsShortcutPath(path)
            ? ConfirmDeleteItemKind.Shortcut
            : ConfirmDeleteItemKind.File;
    }

    private static bool IsShortcutPath(string path)
    {
        string extension = Path.GetExtension(path);

        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetConfirmDeleteItemText(ConfirmDeleteItemKind itemKind)
    {
        return itemKind switch
        {
            ConfirmDeleteItemKind.Folder => "folder",
            ConfirmDeleteItemKind.Shortcut => "shortcut",
            ConfirmDeleteItemKind.Items => "items",
            _ => "file"
        };
    }

    private static string GetConfirmWindowTitle(ConfirmDeleteItemKind itemKind)
    {
        return itemKind switch
        {
            ConfirmDeleteItemKind.Folder => "Delete Folder",
            ConfirmDeleteItemKind.Shortcut => "Delete Shortcut",
            ConfirmDeleteItemKind.Items => "Delete Items",
            _ => "Delete File"
        };
    }

    private string GetFileTypeText(string path)
    {
        string extension = Path.GetExtension(path);
        string displayName = _fileAssociations.ResolveForExtension(extension).DisplayName;

        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.TrimStart('.');

        return string.IsNullOrWhiteSpace(extension)
            ? "File"
            : $"{extension.TrimStart('.').ToUpperInvariant()} File";
    }

    private void UpdateProgressBarValue()
    {
        _progressBar.Value = GetProgressPercent();
    }

    private int GetProgressPercent()
    {
        if (_totalBytes <= 0)
            return _progressComplete ? 100 : 0;

        double percent = _completedBytes * 100d / _totalBytes;
        return (int)Math.Clamp(percent, 0d, 100d);
    }

    private string GetCurrentNameText()
    {
        string name = GetPathDisplayName(_lastSourcePath);
        return string.IsNullOrWhiteSpace(name) ? "Preparing..." : name;
    }

    private string GetItemsRemainingText()
    {
        long remainingItems = _progressComplete
            ? 0
            : Math.Max(0, Math.Max(0, _totalItemCount) - _completedItemCount);

        long remainingBytes = _progressComplete
            ? 0
            : Math.Max(0, _totalBytes - _completedBytes);

        return remainingItems.ToString("N0") + " (" + FormatRemainingSize(remainingBytes) + ")";
    }

    private static string GetPathDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
            return path;

        string name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private static string FormatRemainingSize(long bytes)
    {
        bytes = Math.Max(0, bytes);

        if (bytes < 1024)
            return bytes == 1 ? "1 byte" : bytes.ToString("N0") + " bytes";

        string[] units = ["KB", "MB", "GB", "TB"];
        decimal value = bytes / 1024m;
        int unitIndex = 0;

        while (value >= 1024m && unitIndex < units.Length - 1)
        {
            value /= 1024m;
            unitIndex++;
        }

        return value.ToString(GetScaledSizeFormat(value), CultureInfo.CurrentCulture) + " " + units[unitIndex];
    }

    private static string GetScaledSizeFormat(decimal value)
    {
        if (value >= 100m)
            return "N0";

        if (value >= 10m)
            return "N1";

        return "N2";
    }


    private void UpdateSummarySourceFolder(IReadOnlyList<string> paths)
    {
        string folderPath = GetCommonParentFolderPath(paths);

        _summarySourceFolderPath = folderPath;
        _summarySourceFolderText = string.IsNullOrWhiteSpace(folderPath)
            ? "Multiple locations"
            : GetFolderDisplayName(folderPath);
    }

    private void UpdateSummaryLineText()
    {
        long itemCount = Math.Max(0, _totalItemCount);
        string itemWord = itemCount == 1 ? "item" : "items";

        _lblSummaryPrefix.Text = "Deleting " + itemCount.ToString("N0") + " " + itemWord + " from";
        SetPathLink(_lnkSourceFolder, _summarySourceFolderText, _summarySourceFolderPath);
        LayoutSummaryControls();
    }

    private void SetPathLink(LinkLabel label, string text, string toolTipText)
    {
        if (string.IsNullOrWhiteSpace(text))
            text = "Unknown";

        label.Text = text;
        label.Tag = string.IsNullOrWhiteSpace(toolTipText) ? null : toolTipText;
        label.LinkArea = string.IsNullOrWhiteSpace(toolTipText)
            ? new LinkArea(0, 0)
            : new LinkArea(0, text.Length);

        _pathToolTip.SetToolTip(label, string.IsNullOrWhiteSpace(toolTipText) ? text : toolTipText);
    }

    private static string GetCommonParentFolderPath(IReadOnlyList<string> paths)
    {
        string commonParent = string.Empty;

        foreach (string path in paths)
        {
            string parentPath = GetParentFolderPath(path);
            if (string.IsNullOrWhiteSpace(parentPath))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(commonParent))
            {
                commonParent = parentPath;
                continue;
            }

            if (!PathsEqual(commonParent, parentPath))
                return string.Empty;
        }

        return commonParent;
    }

    private static string GetParentFolderPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? parent = Path.GetDirectoryName(trimmed);
            return parent ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetFolderDisplayName(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return string.Empty;

        string trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
            return folderPath;

        string name = Path.GetFileName(trimmed);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        return trimmed;
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

    private LinkLabel CreatePathLinkLabel()
    {
        LinkLabel label = new()
        {
            AutoSize = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            LinkBehavior = LinkBehavior.HoverUnderline,
            TabStop = false
        };

        label.LinkClicked += PathLink_LinkClicked;
        return label;
    }

    private void PathLink_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (_openFolderInNewWindow is null ||
            sender is not LinkLabel label ||
            label.Tag is not string folderPath ||
            string.IsNullOrWhiteSpace(folderPath) ||
            !Directory.Exists(folderPath))
        {
            return;
        }

        _openFolderInNewWindow(folderPath);
    }

    private void BtnPrimary_Click(object? sender, EventArgs e)
    {
        switch (_mode)
        {
            case DeleteWindowMode.Error:
                CompletePendingErrorAction(ExplorerDeleteErrorAction.Retry);
                ApplyProgressMode();
                break;

            case DeleteWindowMode.ConfirmDelete:
                Action? startDelete = _pendingDeleteStart;
                _pendingDeleteStart = null;
                ApplyProgressMode();
                startDelete?.Invoke();
                break;
        }
    }

    private void BtnSecondary_Click(object? sender, EventArgs e)
    {
        if (_mode != DeleteWindowMode.Error)
            return;

        CompletePendingErrorAction(ExplorerDeleteErrorAction.Skip);
        ApplyProgressMode();
    }

    private void CompletePendingErrorAction(ExplorerDeleteErrorAction action)
    {
        if (_chkDoThisForAll.Visible && _chkDoThisForAll.Checked && action == ExplorerDeleteErrorAction.Skip)
            _applyToAllErrorAction = action;

        _pendingErrorAction?.TrySetResult(action);
        _pendingErrorAction = null;
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        switch (_mode)
        {
            case DeleteWindowMode.Error:
                CompletePendingErrorAction(ExplorerDeleteErrorAction.Cancel);
                CancelOperation();
                break;

            case DeleteWindowMode.ConfirmDelete:
                _pendingDeleteStart = null;
                Close();
                break;
        }
    }

    private void DeleteProgressForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing)
            return;

        _pendingErrorAction?.TrySetResult(ExplorerDeleteErrorAction.Cancel);
        _pendingErrorAction = null;
        _pendingDeleteStart = null;
        _isCancelled = true;
        ClearConfirmIcon();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ReapplyDpiLayoutAndIcon();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ReapplyDpiLayoutAndIcon();
        ShellDialogChrome.CenterOnOwnerScreen(this, _ownerForm);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ReapplyDpiLayoutAndIcon(e.DeviceDpiNew);
        ShellDialogChrome.CenterOnOwnerScreen(this, _ownerForm);
    }

    protected override void Dispose(bool disposing)
    {
        Icon? windowIcon = null;
        Font? bodyFont = null;
        Font? headerFont = null;

        if (disposing)
        {
            ClearConfirmIcon();
            _pathToolTip.Dispose();

            windowIcon = _windowIcon;
            _windowIcon = null;

            bodyFont = _bodyFont;
            _bodyFont = null;

            headerFont = _headerFont;
            _headerFont = null;
        }

        base.Dispose(disposing);

        windowIcon?.Dispose();
        bodyFont?.Dispose();
        headerFont?.Dispose();
    }

    private void CancelOperation()
    {
        if (_isCancelled)
            return;

        _isCancelled = true;
        _btnCancel.Enabled = false;
        _btnCancel.Text = "Cancelling...";
    }

    private static Label CreateEllipsisLabel(bool visible)
    {
        return new Label
        {
            AutoEllipsis = true,
            UseMnemonic = false,
            Visible = visible
        };
    }

    private static Label CreateWrapLabel(bool visible)
    {
        return new TightWrapLabel
        {
            AutoEllipsis = false,
            UseMnemonic = false,
            Visible = visible
        };
    }

    private void CaptureCurrentDpi(int dpi = 0)
    {
        if (dpi <= 0 && IsHandleCreated)
            dpi = DeviceDpi;

        _currentDpi = dpi > 0 ? dpi : DefaultDpi;
    }

    private int ScaleDip(int dip)
    {
        return (int)Math.Round(dip * (_currentDpi / 96f));
    }

    private float ScaleFontPointToPx(float pointSize)
    {
        return pointSize * (_currentDpi / 72f);
    }

    private void ReapplyDpiLayoutAndIcon(int dpi = 0)
    {
        CaptureCurrentDpi(dpi);
        ReapplyDpiMetrics(updateLayout: true);
        RefreshConfirmIcon();
    }

    private void ReapplyDpiMetrics(bool updateLayout)
    {
        RebuildFonts();
        RecalcMetrics();

        if (updateLayout)
            ApplyLayoutMetrics();
    }

    private void RebuildFonts()
    {
        Font baseFont = ShellDialogChrome.DialogFont;
        string familyName = baseFont.FontFamily.Name;

        float bodyFontSizePx = ScaleFontPointToPx(baseFont.SizeInPoints);
        float headerFontSizePx = ScaleFontPointToPx(_mDip.HeaderFontSizePt);

        bool bodyChanged =
            _bodyFont == null ||
            Math.Abs(_lastBodyFontSizePx - bodyFontSizePx) > 0.01f ||
            !string.Equals(_bodyFont.FontFamily.Name, familyName, StringComparison.OrdinalIgnoreCase);

        bool headerChanged =
            _headerFont == null ||
            Math.Abs(_lastHeaderFontSizePx - headerFontSizePx) > 0.01f ||
            !string.Equals(_headerFont.FontFamily.Name, familyName, StringComparison.OrdinalIgnoreCase);

        Font? oldBodyFont = null;
        Font? oldHeaderFont = null;

        if (bodyChanged)
        {
            Font bodyFont = CreateUiPixelFont(familyName, bodyFontSizePx, baseFont.Style);
            oldBodyFont = _bodyFont;

            _bodyFont = bodyFont;
            _lastBodyFontSizePx = bodyFontSizePx;
        }

        if (headerChanged)
        {
            Font headerFont = CreateUiPixelFont(familyName, headerFontSizePx, FontStyle.Regular);
            oldHeaderFont = _headerFont;

            _headerFont = headerFont;
            _lastHeaderFontSizePx = headerFontSizePx;
        }

        ApplyChromeFonts();

        oldBodyFont?.Dispose();
        oldHeaderFont?.Dispose();
    }

    private void RecalcMetrics()
    {
        _mPx = DeleteLayoutMetricsPx.FromDip(
            _mDip,
            ScaleDip,
            Font,
            _headerFont ?? Font);

        ApplyWrappedLabelMetrics();
    }

    private void ApplyWrappedLabelMetrics()
    {
        if (_lblOperation is TightWrapLabel operationLabel)
            operationLabel.LineStepReduction = _mPx.WrappedLineStepReduction;

        if (_lblDetail is TightWrapLabel detailLabel)
            detailLabel.LineStepReduction = _mPx.WrappedLineStepReduction;
    }

    private void ApplyChromeFonts()
    {
        if (_bodyFont != null && !ReferenceEquals(Font, _bodyFont))
            Font = _bodyFont;

        if (_headerFont != null && !ReferenceEquals(_lblOperation.Font, _headerFont))
            _lblOperation.Font = _headerFont;

        if (_bodyFont != null)
        {
            if (!ReferenceEquals(_lblSummaryPrefix.Font, _bodyFont))
                _lblSummaryPrefix.Font = _bodyFont;

            if (!ReferenceEquals(_lnkSourceFolder.Font, _bodyFont))
                _lnkSourceFolder.Font = _bodyFont;
        }
    }

    private void ApplyLayoutMetrics()
    {
        SuspendLayout();
        try
        {
            Size clientSize = new(_mPx.ClientWidth, GetClientHeightForCurrentMode());
            if (ClientSize != clientSize)
                ClientSize = clientSize;

            int headerHeight = GetHeaderHeightForCurrentMode();
            int detailHeight = GetDetailHeightForCurrentMode();

            SetBoundsIfChanged(
                _summaryPanel,
                _mPx.Margin,
                _mPx.HeaderTop,
                _mPx.ContentWidth,
                _mPx.HeaderLineHeight);
            LayoutSummaryControls();

            SetBoundsIfChanged(
                _lblOperation,
                _mPx.Margin,
                _mPx.HeaderTop,
                _mPx.ContentWidth,
                headerHeight);

            SetBoundsIfChanged(
                _picConfirmIcon,
                _mPx.Margin,
                GetConfirmDetailsTopForCurrentMode(),
                _mPx.ConfirmIconSize,
                _mPx.ConfirmIconSize);

            LayoutConfirmDetailRows();

            SetBoundsIfChanged(
                _lblSource,
                _mPx.Margin,
                GetSourceTopForCurrentMode(),
                _mPx.ContentWidth,
                _mPx.BodyLineHeight);

            SetBoundsIfChanged(
                _lblDetail,
                _mPx.Margin,
                GetDetailTopForCurrentMode(),
                _mPx.ContentWidth,
                detailHeight);

            SetBoundsIfChanged(
                _lblStatus,
                _mPx.Margin,
                _mPx.ProgressStatusTop,
                _mPx.ContentWidth,
                _mPx.BodyLineHeight);

            SetBoundsIfChanged(
                _progressBar,
                _mPx.Margin,
                _mPx.ProgressBarTop,
                _mPx.ContentWidth,
                _mPx.ProgressBarHeight);

            SetBoundsIfChanged(
                _lblCurrentName,
                _mPx.Margin,
                _mPx.ProgressNameTop,
                _mPx.ContentWidth,
                _mPx.BodyLineHeight);

            SetBoundsIfChanged(
                _lblItemsRemaining,
                _mPx.Margin,
                _mPx.ProgressItemsTop,
                _mPx.ContentWidth,
                _mPx.BodyLineHeight);

            SetBoundsIfChanged(
                _chkDoThisForAll,
                _mPx.Margin + _mPx.CheckBoxLeftNudge,
                GetErrorCheckBoxTopForCurrentMode(),
                Math.Max(0, _mPx.ContentWidth - _mPx.CheckBoxLeftNudge),
                _mPx.CheckBoxHeight);

            LayoutButtonsForCurrentMode();
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void LayoutSummaryControls()
    {
        int x = 0;

        LayoutSummaryText(_lblSummaryPrefix, ref x);
        LayoutSummaryLink(_lnkSourceFolder, ref x);

        int bottom = Math.Max(_lblSummaryPrefix.Bottom, _lnkSourceFolder.Bottom);
        int panelHeight = Math.Max(_mPx.HeaderLineHeight, bottom);

        if (_summaryPanel.Height != panelHeight)
            _summaryPanel.Height = panelHeight;
    }

    private void LayoutSummaryText(Label label, ref int x)
    {
        label.Left = x;
        label.Top = _mPx.SummaryTextTopMargin;
        label.Height = _mPx.BodyLineHeight;

        x = label.Right + _mPx.SummaryInlineGap;
    }

    private void LayoutSummaryLink(LinkLabel label, ref int x)
    {
        label.Left = x;
        label.Top = _mPx.SummaryLinkTopMargin;
        label.Height = _mPx.BodyLineHeight;

        x = label.Right + _mPx.SummaryInlineGap;
    }

    private void LayoutConfirmDetailRows()
    {
        int infoTop = GetConfirmDetailsTopForCurrentMode();
        int textLeft = _mPx.ConfirmTextLeft;
        int textWidth = _mPx.ConfirmTextWidth;
        int labelHeight = _mPx.ConfirmLineHeight;
        int rowStep = _mPx.ConfirmRowStep;

        SetBoundsIfChanged(
            _lblConfirmName,
            textLeft,
            infoTop,
            textWidth,
            labelHeight);

        SetBoundsIfChanged(
            _lblConfirmType,
            textLeft,
            infoTop + rowStep,
            textWidth,
            labelHeight);

        SetBoundsIfChanged(
            _lblConfirmSize,
            textLeft,
            infoTop + (rowStep * 2),
            textWidth,
            labelHeight);

        int modifiedRow = _confirmDetailsActive && !_confirmDetailsIsDirectory ? 3 : 1;
        SetBoundsIfChanged(
            _lblConfirmModified,
            textLeft,
            infoTop + (rowStep * modifiedRow),
            textWidth,
            labelHeight);
    }

    private void LayoutButtonsForCurrentMode()
    {
        if (_mode == DeleteWindowMode.Progress)
            return;

        int buttonTop = GetButtonTopForCurrentMode();

        switch (_mode)
        {
            case DeleteWindowMode.Error when _errorIsSingleFileDelete:
                // For a single-file delete error, match the two-button spacing by
                // placing Try Again where Skip would normally sit.
                SetButtonBounds(_btnPrimary, GetRightAlignedButtonLeft(0, 2), buttonTop);
                SetButtonBounds(_btnCancel, GetRightAlignedButtonLeft(1, 2), buttonTop);
                break;

            case DeleteWindowMode.Error:
                SetButtonBounds(_btnPrimary, GetRightAlignedButtonLeft(0, 3), buttonTop);
                SetButtonBounds(_btnSecondary, GetRightAlignedButtonLeft(1, 3), buttonTop);
                SetButtonBounds(_btnCancel, GetRightAlignedButtonLeft(2, 3), buttonTop);
                break;

            case DeleteWindowMode.ConfirmDelete:
                SetButtonBounds(_btnPrimary, GetRightAlignedButtonLeft(0, 2), buttonTop);
                SetButtonBounds(_btnCancel, GetRightAlignedButtonLeft(1, 2), buttonTop);
                break;

            default:
                SetButtonBounds(_btnCancel, GetRightAlignedButtonLeft(0, 1), buttonTop);
                break;
        }
    }

    private int GetClientHeightForCurrentMode()
    {
        int contentBottom = GetContentBlockBottomForCurrentMode();

        if (_mode == DeleteWindowMode.Progress)
            return contentBottom + _mPx.Margin;

        return GetButtonTopForCurrentMode() + _mPx.ButtonHeight + _mPx.ButtonVerticalGap;
    }

    private int GetButtonTopForCurrentMode()
    {
        int contentBottom = GetContentBlockBottomForCurrentMode();
        int gap = _mode == DeleteWindowMode.Error && _chkDoThisForAll.Visible
            ? _mPx.CheckBoxToButtonsGap
            : _mPx.ButtonVerticalGap;

        return contentBottom + gap;
    }

    private int GetContentBlockBottomForCurrentMode()
    {
        return _mode switch
        {
            DeleteWindowMode.Error when _chkDoThisForAll.Visible => GetErrorCheckBoxTopForCurrentMode() + _mPx.CheckBoxHeight,
            DeleteWindowMode.Error when _confirmDetailsActive => GetConfirmDetailsTopForCurrentMode() + _mPx.ConfirmBlockHeight,
            DeleteWindowMode.Error => GetDetailTopForCurrentMode() + GetDetailHeightForCurrentMode(),
            DeleteWindowMode.ConfirmDelete when _confirmDetailsActive => GetConfirmDetailsTopForCurrentMode() + _mPx.ConfirmBlockHeight,
            DeleteWindowMode.ConfirmDelete => GetSourceTopForCurrentMode() + _mPx.BodyLineHeight,
            _ => _mPx.ProgressItemsTop + _mPx.BodyLineHeight
        };
    }

    private int GetHeaderHeightForCurrentMode()
    {
        if (_mode == DeleteWindowMode.Progress || !_lblOperation.Visible)
            return _mPx.HeaderLineHeight;

        return MeasureWrappedLabelHeight(
            _lblOperation,
            _mPx.ContentWidth,
            _mPx.HeaderLineHeight);
    }

    private int GetSourceTopForCurrentMode()
    {
        if (_mode == DeleteWindowMode.Progress)
            return _mPx.ProgressSourceTop;

        return _mPx.HeaderTop + GetHeaderHeightForCurrentMode() + _mPx.HeaderToBodyGap;
    }

    private int GetDetailTopForCurrentMode()
    {
        return _mode == DeleteWindowMode.Error
            ? GetSourceTopForCurrentMode()
            : _mPx.ProgressDetailTop;
    }

    private int GetDetailHeightForCurrentMode()
    {
        if (_mode != DeleteWindowMode.Error || !_lblDetail.Visible)
            return _mPx.BodyLineHeight;

        return MeasureWrappedLabelHeight(
            _lblDetail,
            _mPx.ContentWidth,
            _mPx.BodyLineHeight);
    }

    private int GetErrorCheckBoxTopForCurrentMode()
    {
        int previousBottom = _mode == DeleteWindowMode.Error && _confirmDetailsActive
            ? GetConfirmDetailsTopForCurrentMode() + _mPx.ConfirmBlockHeight
            : GetDetailTopForCurrentMode() + GetDetailHeightForCurrentMode();

        return previousBottom + _mPx.ButtonVerticalGap;
    }

    private int GetConfirmDetailsTopForCurrentMode()
    {
        if (_mode != DeleteWindowMode.Error)
            return _mPx.HeaderTop + GetHeaderHeightForCurrentMode() + _mPx.ConfirmHeaderGap;

        return GetDetailTopForCurrentMode() + GetDetailHeightForCurrentMode() + _mPx.ErrorDetailToDetailsGap;
    }

    private void SetButtonBounds(Button button, int left, int top)
    {
        SetBoundsIfChanged(
            button,
            left,
            top,
            _mPx.ButtonWidth,
            _mPx.ButtonHeight);
    }

    private int GetRightAlignedButtonLeft(int index, int count)
    {
        return _mPx.ClientWidth -
               _mPx.ButtonRightMargin -
               ((count - index) * _mPx.ButtonWidth) -
               ((count - 1 - index) * _mPx.ButtonGap);
    }

    private void RefreshConfirmIcon()
    {
        if (!_confirmDetailsActive || string.IsNullOrWhiteSpace(_confirmIconPath))
            return;

        SetConfirmIcon(_confirmIconPath, _confirmIconIsDirectory);
    }

    private static int MeasureWrappedLabelHeight(Label label, int width, int minHeight)
    {
        if (width <= 0 || string.IsNullOrWhiteSpace(label.Text))
            return minHeight;

        if (label is TightWrapLabel tightLabel)
            return tightLabel.GetWrappedHeight(width, minHeight);

        Size measured = TextRenderer.MeasureText(
            label.Text,
            label.Font,
            new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);

        return Math.Max(minHeight, measured.Height);
    }

    private static void SetBoundsIfChanged(Control control, int x, int y, int width, int height)
    {
        Rectangle bounds = new(x, y, width, height);

        if (control.Bounds != bounds)
            control.Bounds = bounds;
    }

    private static Font CreateUiPixelFont(string familyName, float size, FontStyle style)
    {
        float safeSize = size > 0f ? size : 12f;

        try
        {
            return new Font(familyName, safeSize, style, GraphicsUnit.Pixel);
        }
        catch (ArgumentException)
        {
            return new Font(FontFamily.GenericSansSerif, safeSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }
    }

    private void EnsureShownCore()
    {
        ShellDialogChrome.ShowCenteredNonModal(this, _ownerForm);
    }

    private bool RunOnUiThread(Action action)
    {
        if (IsDisposed)
            return false;

        try
        {
            if (IsHandleCreated && InvokeRequired)
            {
                Invoke(action);
                return true;
            }

            action();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class TightWrapLabel : Label
    {
        private static readonly Size SingleLineMeasureSize = new(32767, 32767);

        private static readonly TextFormatFlags SingleLineMeasureFlags =
            TextFormatFlags.SingleLine | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;

        private static readonly TextFormatFlags DrawFlags =
            TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;

        private int _lineStepReduction;

        public TightWrapLabel()
        {
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public int LineStepReduction
        {
            get => _lineStepReduction;
            set
            {
                int safeValue = Math.Max(0, value);
                if (_lineStepReduction == safeValue)
                    return;

                _lineStepReduction = safeValue;
                Invalidate();
            }
        }

        public int GetWrappedHeight(int width, int minHeight)
        {
            if (width <= 0 || string.IsNullOrWhiteSpace(Text))
                return minHeight;

            List<string> lines = GetWrappedLines(Text, Font, width);
            if (lines.Count <= 1)
                return minHeight;

            int lineHeight = GetLineHeight(Font);
            int lineStep = GetLineStep(Font, LineStepReduction);
            return Math.Max(minHeight, lineHeight + (lineStep * (lines.Count - 1)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaintBackground(e);

            if (string.IsNullOrEmpty(Text))
                return;

            List<string> lines = GetWrappedLines(Text, Font, ClientSize.Width);
            if (lines.Count == 0)
                return;

            Color textColor = Enabled ? ForeColor : SystemColors.GrayText;
            int lineHeight = GetLineHeight(Font);
            int lineStep = GetLineStep(Font, LineStepReduction);
            int y = 0;

            foreach (string line in lines)
            {
                Rectangle bounds = new(0, y, ClientSize.Width, lineHeight);
                TextRenderer.DrawText(e.Graphics, line, Font, bounds, textColor, DrawFlags);
                y += lineStep;

                if (y >= ClientSize.Height)
                    break;
            }
        }

        private static int GetLineHeight(Font font)
        {
            Size measured = TextRenderer.MeasureText("Hg", font, SingleLineMeasureSize, SingleLineMeasureFlags);
            return Math.Max(font.Height, measured.Height);
        }

        private static int GetLineStep(Font font, int reduction)
        {
            int lineHeight = GetLineHeight(font);
            return Math.Max(1, lineHeight - reduction);
        }

        private static List<string> GetWrappedLines(string text, Font font, int width)
        {
            List<string> lines = new();
            if (width <= 0 || string.IsNullOrEmpty(text))
                return lines;

            string normalizedText = text.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (string paragraph in normalizedText.Split('\n'))
                AddWrappedParagraph(lines, paragraph, font, width);

            return lines;
        }

        private static void AddWrappedParagraph(List<string> lines, string paragraph, Font font, int width)
        {
            string[] words = paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                lines.Add(string.Empty);
                return;
            }

            string currentLine = string.Empty;
            foreach (string word in words)
            {
                string candidate = string.IsNullOrEmpty(currentLine)
                    ? word
                    : currentLine + " " + word;

                if (FitsLine(candidate, font, width))
                {
                    currentLine = candidate;
                    continue;
                }

                if (!string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                    currentLine = string.Empty;
                }

                if (FitsLine(word, font, width))
                {
                    currentLine = word;
                    continue;
                }

                AddBrokenWord(lines, word, font, width, ref currentLine);
            }

            if (!string.IsNullOrEmpty(currentLine))
                lines.Add(currentLine);
        }

        private static void AddBrokenWord(List<string> lines, string word, Font font, int width, ref string currentLine)
        {
            string remaining = word;
            while (remaining.Length > 0)
            {
                int take = GetMaxFittingPrefixLength(remaining, font, width);
                if (take <= 0)
                    take = 1;

                string part = remaining[..take];
                remaining = remaining[take..];

                if (remaining.Length == 0)
                    currentLine = part;
                else
                    lines.Add(part);
            }
        }

        private static int GetMaxFittingPrefixLength(string text, Font font, int width)
        {
            int low = 1;
            int high = text.Length;
            int best = 0;

            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                if (FitsLine(text[..mid], font, width))
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return best;
        }

        private static bool FitsLine(string text, Font font, int width)
        {
            Size measured = TextRenderer.MeasureText(text, font, SingleLineMeasureSize, SingleLineMeasureFlags);
            return measured.Width <= width;
        }
    }

    // Base values are stored in DIPs and converted to pixels from the current
    // DeviceDpi. Text row heights are also checked against the actual font
    // heights so WinPE font metrics cannot clip individual label rows.
    private sealed class DeleteLayoutMetrics
    {
        public int ClientWidthDip { get; init; } = 450;

        public int MarginDip { get; init; } = 12;
        public int RightMarginDip { get; init; } = 28;

        public int HeaderTopDip { get; init; } = 6;
        public int HeaderLineHeightDip { get; init; } = 22;
        public int HeaderToBodyGapDip { get; init; } = 12;
        public int SummaryToBodyGapDip { get; init; } = 6;
        public int ErrorDetailToDetailsGapDip { get; init; } = 0;

        public int BodyLineHeightDip { get; init; } = 18;
        public int FontHeightPaddingDip { get; init; } = 4;
        public int WrappedLineStepReductionDip { get; init; } = 6;

        public int ConfirmIconTextGapDip { get; init; } = 16;
        public int ConfirmHeaderGapDip { get; init; } = 4;
        public int ConfirmLineHeightDip { get; init; } = 16;
        public int ConfirmFontHeightPaddingDip { get; init; } = 1;
        public int ConfirmRowCount { get; init; } = 4;

        public int ProgressBodyRowGapDip { get; init; } = 8;
        public int ProgressStatusToBarGapDip { get; init; } = 0;
        public int ProgressBarHeightDip { get; init; } = 22;
        public int CheckBoxLeftNudgeDip { get; init; } = 6;
        public int CheckBoxToButtonsGapDip { get; init; } = 2;

        public int ButtonVerticalGapDip { get; init; } = 12;

        public int ButtonWidthDip { get; init; } = 80;
        public int ButtonHeightDip { get; init; } = 25;
        public int ButtonGapDip { get; init; } = 10;
        public float HeaderFontSizePt { get; init; } = 10.5f;
    }

    private sealed class DeleteLayoutMetricsPx
    {
        public int ClientWidth { get; init; }
        public int Margin { get; init; }
        public int RightMargin { get; init; }
        public int ContentWidth { get; init; }

        public int HeaderTop { get; init; }
        public int HeaderLineHeight { get; init; }
        public int HeaderToBodyGap { get; init; }
        public int SummaryToBodyGap { get; init; }
        public int BodyLineHeight { get; init; }
        public int WrappedLineStepReduction { get; init; }
        public int SummaryTextTopMargin { get; init; }
        public int SummaryLinkTopMargin { get; init; }
        public int SummaryInlineGap { get; init; }

        public int ConfirmIconSize { get; init; }
        public int ConfirmHeaderGap { get; init; }
        public int ConfirmLineHeight { get; init; }
        public int ConfirmRowStep { get; init; }
        public int ConfirmBlockHeight { get; init; }
        public int ConfirmTextLeft { get; init; }
        public int ConfirmTextWidth { get; init; }

        public int ProgressSourceTop { get; init; }
        public int ProgressDetailTop { get; init; }
        public int ProgressStatusTop { get; init; }
        public int ProgressBarTop { get; init; }
        public int ProgressNameTop { get; init; }
        public int ProgressItemsTop { get; init; }
        public int ProgressBarHeight { get; init; }
        public int ProgressBodyRowGap { get; init; }
        public int ErrorDetailToDetailsGap { get; init; }
        public int CheckBoxHeight { get; init; }
        public int CheckBoxLeftNudge { get; init; }
        public int CheckBoxToButtonsGap { get; init; }

        public int ButtonVerticalGap { get; init; }
        public int ButtonWidth { get; init; }
        public int ButtonHeight { get; init; }
        public int ButtonGap { get; init; }
        public int ButtonRightMargin { get; init; }

        public static DeleteLayoutMetricsPx FromDip(
            DeleteLayoutMetrics dip,
            Func<int, int> scale,
            Font bodyFont,
            Font headerFont)
        {
            int clientWidth = scale(dip.ClientWidthDip);
            int margin = scale(dip.MarginDip);
            int rightMargin = scale(dip.RightMarginDip);

            int headerLineHeight = Math.Max(
                scale(dip.HeaderLineHeightDip),
                headerFont.Height + scale(dip.FontHeightPaddingDip));

            int bodyLineHeight = Math.Max(
                scale(dip.BodyLineHeightDip),
                bodyFont.Height + scale(dip.FontHeightPaddingDip));

            int headerTop = scale(dip.HeaderTopDip);
            int confirmLineHeight = Math.Max(
                scale(dip.ConfirmLineHeightDip),
                bodyFont.Height + scale(dip.ConfirmFontHeightPaddingDip));

            int confirmRowStep = GetTightRowStep(
                confirmLineHeight,
                bodyFont,
                scale(2));

            int confirmBlockHeight =
                confirmLineHeight + (confirmRowStep * (dip.ConfirmRowCount - 1));
            int confirmHeaderGap = scale(dip.ConfirmHeaderGapDip);
            int confirmIconSize = confirmBlockHeight;
            int confirmTextLeft = margin + confirmIconSize + scale(dip.ConfirmIconTextGapDip);

            int progressStatusToBarGap = scale(dip.ProgressStatusToBarGapDip);
            int progressBodyRowGap = scale(dip.ProgressBodyRowGapDip);
            int progressTextRowStep = GetTightRowStep(
            bodyLineHeight,
            bodyFont,
            scale(2));

            int headerToBodyGap = scale(dip.HeaderToBodyGapDip);
            int summaryToBodyGap = scale(dip.SummaryToBodyGapDip);
            int errorDetailToDetailsGap = scale(dip.ErrorDetailToDetailsGapDip);
            int progressSourceTop = headerTop + headerLineHeight + summaryToBodyGap;
            int progressDetailTop = progressSourceTop + bodyLineHeight + progressBodyRowGap;
            int progressStatusTop = progressSourceTop;
            int progressBarHeight = scale(dip.ProgressBarHeightDip);
            int progressBarTop = progressStatusTop + bodyLineHeight + progressStatusToBarGap;
            int progressNameTop = progressBarTop + progressBarHeight + progressBodyRowGap;
            int progressItemsTop = progressNameTop + progressTextRowStep;
            int checkBoxHeight = bodyLineHeight;

            int buttonHeight = Math.Max(
                scale(dip.ButtonHeightDip),
                bodyFont.Height + scale(10));
            int buttonVerticalGap = scale(dip.ButtonVerticalGapDip);

            return new DeleteLayoutMetricsPx
            {
                ClientWidth = clientWidth,

                Margin = margin,
                RightMargin = rightMargin,
                ContentWidth = clientWidth - margin - rightMargin,

                HeaderTop = headerTop,
                HeaderLineHeight = headerLineHeight,
                HeaderToBodyGap = headerToBodyGap,
                SummaryToBodyGap = summaryToBodyGap,
                BodyLineHeight = bodyLineHeight,
                WrappedLineStepReduction = scale(dip.WrappedLineStepReductionDip),
                SummaryTextTopMargin = scale(SummaryTextTopMarginDip),
                SummaryLinkTopMargin = scale(SummaryLinkTopMarginDip),
                SummaryInlineGap = scale(SummaryInlineGapDip),

                ConfirmIconSize = confirmIconSize,
                ConfirmHeaderGap = confirmHeaderGap,
                ConfirmLineHeight = confirmLineHeight,
                ConfirmRowStep = confirmRowStep,
                ConfirmBlockHeight = confirmBlockHeight,
                ConfirmTextLeft = confirmTextLeft,
                ConfirmTextWidth = clientWidth - confirmTextLeft - rightMargin,

                ProgressSourceTop = progressSourceTop,
                ProgressDetailTop = progressDetailTop,
                ProgressStatusTop = progressStatusTop,
                ProgressBarTop = progressBarTop,
                ProgressNameTop = progressNameTop,
                ProgressItemsTop = progressItemsTop,
                ProgressBarHeight = progressBarHeight,
                ProgressBodyRowGap = progressBodyRowGap,
                ErrorDetailToDetailsGap = errorDetailToDetailsGap,
                CheckBoxHeight = checkBoxHeight,
                CheckBoxLeftNudge = scale(dip.CheckBoxLeftNudgeDip),
                CheckBoxToButtonsGap = scale(dip.CheckBoxToButtonsGapDip),

                ButtonVerticalGap = buttonVerticalGap,
                ButtonWidth = scale(dip.ButtonWidthDip),
                ButtonHeight = buttonHeight,
                ButtonGap = scale(dip.ButtonGapDip),
                ButtonRightMargin = rightMargin
            };
        }

        private static int GetTightRowStep(int lineHeight, Font font, int reduction)
        {
            int safeReduction = Math.Max(0, reduction);
            int minimumStep = Math.Max(1, lineHeight - safeReduction);
            int fontStep = Math.Max(1, font.Height - safeReduction);

            return Math.Min(lineHeight, Math.Max(minimumStep, fontStep));
        }
    }
}
