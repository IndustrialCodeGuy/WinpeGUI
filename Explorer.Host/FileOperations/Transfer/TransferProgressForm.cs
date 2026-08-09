using Shared.Shell.Utilities;
using Shell.Core.FileTypes;
using System.ComponentModel;
using System.Globalization;
using UiExplorerIconCache = Explorer.UI.Icons.ExplorerIconCache;

namespace Explorer.Host.FileOperations.Transfer;

internal sealed class TransferProgressForm : Form, IExplorerTransferProgressSink
{
    private enum TransferWindowMode
    {
        Progress,
        Conflict,
        Error,
        Compare
    }

    private readonly TransferLayoutMetrics _mDip = new();
    private TransferLayoutMetricsPx _mPx = new();

    private const int SummaryTextTopMarginDip = 3;
    private const int SummaryLinkTopMarginDip = 4;
    private const int SummaryInlineGapDip = -3;
    private const int OverwriteAllButtonWidthDip = 96;
    private const int CompareFilesButtonWidthDip = 104;
    private const int DefaultDpi = 96;

    private readonly Panel _summaryPanel;
    private readonly Label _lblSummaryPrefix;
    private readonly LinkLabel _lnkSourceFolder;
    private readonly Label _lblSummaryMiddle;
    private readonly LinkLabel _lnkDestinationFolder;
    private readonly Label _lblOperation;
    private readonly Label _lblDetail;
    private readonly PictureBox _picErrorIcon;
    private readonly Label _lblErrorName;
    private readonly Label _lblErrorType;
    private readonly Label _lblErrorSize;
    private readonly Label _lblErrorModified;
    private readonly CheckBox _chkDoThisForAll;
    private readonly Label _lblStatus;
    private readonly ProgressBar _progressBar;
    private readonly Label _lblCurrentName;
    private readonly Label _lblItemsRemaining;
    private readonly Button _btnPrimary;
    private readonly Button _btnSecondary;
    private readonly Button _btnCancel;
    private readonly ToolTip _pathToolTip;
    private readonly IExplorerFileAssociationService _fileAssociations;
    private readonly Action<string>? _openFolderInNewWindow;
    private readonly SynchronizationContext? _uiContext;

    private readonly bool _move;
    private readonly string _windowTitle;

    private Font? _bodyFont;
    private Font? _headerFont;
    private float _lastBodyFontSizePx;
    private float _lastHeaderFontSizePx;
    private int _currentDpi = DefaultDpi;
    private int _primaryButtonWidthDip;
    private int _secondaryButtonWidthDip;
    private int _cancelButtonWidthDip;

    private ExplorerTransferConflictAction? _applyConflictActionToAll;
    private Dictionary<string, ExplorerTransferConflictAction>? _compareConflictActions;
    private ExplorerTransferErrorAction? _applyToAllErrorAction;
    private bool _isCancelled;
    private bool _errorSkipVisible;

    private TransferWindowMode _mode = TransferWindowMode.Progress;
    private TaskCompletionSource<ExplorerTransferConflictDecision>? _pendingConflictDecision;
    private TaskCompletionSource<ExplorerTransferErrorAction>? _pendingErrorAction;

    private ExplorerTransferSummary _summary = ExplorerTransferSummary.Empty;
    private string _lastSourcePath = string.Empty;
    private string _lastDestinationPath = string.Empty;
    private string _activeConflictSourcePath = string.Empty;
    private string _activeConflictDestinationPath = string.Empty;
    private long _totalBytes;
    private long _completedBytes;
    private long _completedItemCount;
    private bool _progressComplete;

    private string _errorIconPath = string.Empty;
    private bool _errorIconIsDirectory;
    private bool _errorDetailsActive;
    private bool _errorDetailsIsDirectory;

    private bool _updatingMainLayout;
    private TransferConflictCompareView? _compareView;
    private Form? _referenceOwnerForm;
    private System.Windows.Forms.Timer? _showDelayTimer;

    public bool IsCancelled => _isCancelled;

    public TransferProgressForm(
        bool move,
        IExplorerFileAssociationService fileAssociations,
        Action<string>? openFolderInNewWindow = null)
    {
        _move = move;
        _windowTitle = move ? "Moving" : "Copying";
        _fileAssociations = fileAssociations ?? throw new ArgumentNullException(nameof(fileAssociations));
        _openFolderInNewWindow = openFolderInNewWindow;
        _uiContext = SynchronizationContext.Current;
        _primaryButtonWidthDip = _mDip.ButtonWidthDip;
        _secondaryButtonWidthDip = _mDip.ButtonWidthDip;
        _cancelButtonWidthDip = _mDip.ButtonWidthDip;

        Text = _windowTitle;
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
            Padding = Padding.Empty
        };

        _lblSummaryPrefix = new Label
        {
            AutoSize = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            UseMnemonic = false
        };

        _lnkSourceFolder = CreatePathLinkLabel();

        _lblSummaryMiddle = new Label
        {
            AutoSize = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Text = "to",
            UseMnemonic = false
        };

        _lnkDestinationFolder = CreatePathLinkLabel();

        _lblOperation = CreateWrapLabel(visible: false);
        _lblDetail = CreateWrapLabel(visible: false);

        _picErrorIcon = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.CenterImage,
            Visible = false
        };

        _lblErrorName = CreateEllipsisLabel(visible: false);
        _lblErrorType = CreateEllipsisLabel(visible: false);
        _lblErrorSize = CreateEllipsisLabel(visible: false);
        _lblErrorModified = CreateEllipsisLabel(visible: false);

        _chkDoThisForAll = new CheckBox
        {
            Text = "Do this for all current items",
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Visible = false
        };

        _lblStatus = CreateEllipsisLabel(visible: true);
        _lblStatus.Text = "0% complete";

        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        _lblCurrentName = CreateEllipsisLabel(visible: true);
        _lblCurrentName.Text = "Name: Preparing...";

        _lblItemsRemaining = CreateEllipsisLabel(visible: true);
        _lblItemsRemaining.Text = "Items Remaining: 0 (0 bytes)";

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
        _summaryPanel.Controls.Add(_lblSummaryMiddle);
        _summaryPanel.Controls.Add(_lnkDestinationFolder);

        Controls.Add(_summaryPanel);
        Controls.Add(_lblOperation);
        Controls.Add(_lblDetail);
        Controls.Add(_picErrorIcon);
        Controls.Add(_lblErrorName);
        Controls.Add(_lblErrorType);
        Controls.Add(_lblErrorSize);
        Controls.Add(_lblErrorModified);
        Controls.Add(_chkDoThisForAll);
        Controls.Add(_lblStatus);
        Controls.Add(_progressBar);
        Controls.Add(_lblCurrentName);
        Controls.Add(_lblItemsRemaining);
        Controls.Add(_btnPrimary);
        Controls.Add(_btnSecondary);
        Controls.Add(_btnCancel);

        AcceptButton = _btnPrimary;
        CancelButton = _btnCancel;

        ReapplyDpiMetrics(updateLayout: false);
        UpdateSummaryLineText();
        ApplyProgressMode();

        FormClosing += TransferProgressForm_FormClosing;
    }

    public void StartDeferredShow(Form? referenceOwner, int delayMs = 200)
    {
        RunOnUiThread(() =>
        {
            _referenceOwnerForm = referenceOwner;
            StopShowDelayTimer();

            if (!IsHandleCreated)
            {
                _ = Handle;
            }

            if (delayMs <= 0)
            {
                EnsureShownCore();
                return;
            }

            _showDelayTimer = new System.Windows.Forms.Timer
            {
                Interval = delayMs
            };

            _showDelayTimer.Tick += (_, _) =>
            {
                StopShowDelayTimer();

                if (!_isCancelled &&
                    !IsDisposed &&
                    !Visible &&
                    _mode == TransferWindowMode.Progress)
                {
                    EnsureShownCore();
                }
            };

            _showDelayTimer.Start();
        });
    }

    public void InitializeProgress(ExplorerTransferSummary summary)
    {
        RunOnUiThread(() =>
        {
            _summary = summary ?? ExplorerTransferSummary.Empty;
            _totalBytes = Math.Max(0, _summary.TotalBytes);
            _completedBytes = 0;
            _completedItemCount = 0;
            _progressComplete = false;
            _applyToAllErrorAction = null;
            _errorSkipVisible = false;
            _chkDoThisForAll.Visible = false;
            _chkDoThisForAll.Checked = false;
            UpdateSummaryLineText();
            UpdateProgressBarValue();
            ApplyLastProgressText();
        });
    }

    public void ReportProgress(string operation, string sourcePath, string destinationPath)
    {
        _lastSourcePath = sourcePath ?? string.Empty;
        _lastDestinationPath = destinationPath ?? string.Empty;

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
            _completedItemCount = Math.Max(0, _summary.TotalItemCount);
            _progressComplete = true;
            UpdateProgressBarValue();
            ApplyLastProgressText();
        });
    }

    public ExplorerTransferConflictDecision ResolveConflict(string sourcePath, string destinationPath)
    {
        if (_applyConflictActionToAll.HasValue)
        {
            return new ExplorerTransferConflictDecision
            {
                Action = _applyConflictActionToAll.Value,
                ApplyToAll = true
            };
        }

        if (_compareConflictActions != null &&
            _compareConflictActions.TryGetValue(sourcePath, out ExplorerTransferConflictAction compareAction))
        {
            return new ExplorerTransferConflictDecision
            {
                Action = compareAction,
                ApplyToAll = true
            };
        }

        TaskCompletionSource<ExplorerTransferConflictDecision> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!RunOnUiThread(() => ShowConflictMode(sourcePath, destinationPath, tcs)))
        {
            return new ExplorerTransferConflictDecision
            {
                Action = ExplorerTransferConflictAction.Cancel
            };
        }

        return tcs.Task.GetAwaiter().GetResult();
    }

    public ExplorerTransferErrorAction HandleError(string sourcePath, string destinationPath, Exception exception, bool allowSkip)
    {
        if (allowSkip && _applyToAllErrorAction.HasValue)
            return _applyToAllErrorAction.Value;

        TaskCompletionSource<ExplorerTransferErrorAction> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!RunOnUiThread(() => ShowErrorMode(sourcePath, destinationPath, exception, allowSkip, tcs)))
            return ExplorerTransferErrorAction.Cancel;

        return tcs.Task.GetAwaiter().GetResult();
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

    private void ShowConflictMode(
        string sourcePath,
        string destinationPath,
        TaskCompletionSource<ExplorerTransferConflictDecision> tcs)
    {
        DisposeCompareView();
        ClearErrorDetails();

        _pendingErrorAction = null;
        _chkDoThisForAll.Visible = false;
        _chkDoThisForAll.Checked = false;
        _errorSkipVisible = false;
        _pendingConflictDecision = tcs;
        _activeConflictSourcePath = sourcePath ?? string.Empty;
        _activeConflictDestinationPath = destinationPath ?? string.Empty;
        _mode = TransferWindowMode.Conflict;
        SetMainLayoutVisible(true);

        Text = _windowTitle;
        _lblOperation.Text = BuildConflictStatusText(sourcePath);
        _lblDetail.Text = string.Empty;
        string pathToolTip = BuildTransferPathToolTip(sourcePath, destinationPath);
        _pathToolTip.SetToolTip(_lblOperation, pathToolTip);
        _pathToolTip.SetToolTip(_lblDetail, string.Empty);
        _pathToolTip.SetToolTip(_lblStatus, string.Empty);

        SetButtonWidths(
            primaryDip: OverwriteAllButtonWidthDip,
            secondaryDip: _mDip.ButtonWidthDip,
            cancelDip: CompareFilesButtonWidthDip);

        _btnPrimary.Text = "Overwrite All";
        _btnPrimary.Visible = true;

        _btnSecondary.Text = "Skip All";
        _btnSecondary.Visible = true;

        _btnCancel.Text = "Compare Files";
        _btnCancel.Visible = true;
        _btnCancel.Enabled = true;

        AcceptButton = null;
        CancelButton = null;

        ApplyLayoutMetrics();
        EnsureShownCore();
    }

    private void ShowErrorMode(
        string sourcePath,
        string destinationPath,
        Exception exception,
        bool allowSkip,
        TaskCompletionSource<ExplorerTransferErrorAction> tcs)
    {
        DisposeCompareView();

        _pendingConflictDecision = null;
        _pendingErrorAction = tcs;
        _mode = TransferWindowMode.Error;

        bool isDirectory = Directory.Exists(sourcePath);
        _errorSkipVisible = allowSkip && !_summary.IsSingleTopLevelFile;
        ApplyErrorDetails(sourcePath, isDirectory);
        SetMainLayoutVisible(true);

        Text = _windowTitle;
        GetTransferErrorText(exception, out string header, out string detail);
        _lblOperation.Text = header;
        _lblDetail.Text = detail;
        string pathToolTip = BuildTransferPathToolTip(sourcePath, destinationPath);
        _pathToolTip.SetToolTip(_lblOperation, pathToolTip);
        _pathToolTip.SetToolTip(_lblDetail, pathToolTip);
        _pathToolTip.SetToolTip(_lblStatus, string.Empty);

        _chkDoThisForAll.Checked = false;
        _chkDoThisForAll.Visible = _errorSkipVisible;

        SetButtonWidths(
            primaryDip: _mDip.ButtonWidthDip,
            secondaryDip: _mDip.ButtonWidthDip,
            cancelDip: _mDip.ButtonWidthDip);

        _btnPrimary.Text = "Try Again";
        _btnPrimary.Visible = true;

        _btnSecondary.Text = "Skip";
        _btnSecondary.Visible = _errorSkipVisible;

        _btnCancel.Text = "Cancel";
        _btnCancel.Visible = true;
        _btnCancel.Enabled = true;

        AcceptButton = _btnPrimary;
        CancelButton = _btnCancel;

        ApplyLayoutMetrics();
        EnsureShownCore();
    }

    private void ApplyProgressMode()
    {
        DisposeCompareView();
        ClearErrorDetails();
        _mode = TransferWindowMode.Progress;
        SetMainLayoutVisible(true);

        Text = _windowTitle;
        SetButtonWidths(
            primaryDip: _mDip.ButtonWidthDip,
            secondaryDip: _mDip.ButtonWidthDip,
            cancelDip: _mDip.ButtonWidthDip);
        _chkDoThisForAll.Visible = false;
        _chkDoThisForAll.Checked = false;
        _errorSkipVisible = false;
        _btnPrimary.Visible = false;
        _btnSecondary.Visible = false;
        _btnCancel.Text = "Cancel";
        _btnCancel.Visible = true;
        _btnCancel.Enabled = true;
        AcceptButton = _btnPrimary;
        CancelButton = _btnCancel;

        ApplyLastProgressText();
        ApplyLayoutMetrics();
    }

    private void ApplyLastProgressText()
    {
        if (_mode != TransferWindowMode.Progress)
            return;

        UpdateSummaryLineText();

        _lblStatus.Text = $"{GetProgressPercent()}% complete";
        _lblCurrentName.Text = "Name: " + GetCurrentNameText();
        _lblItemsRemaining.Text = "Items Remaining: " + GetItemsRemainingText();

        _pathToolTip.SetToolTip(_lblStatus, string.Empty);
        _pathToolTip.SetToolTip(_lblCurrentName, string.IsNullOrWhiteSpace(_lastSourcePath) ? string.Empty : _lastSourcePath);
        _pathToolTip.SetToolTip(_lblItemsRemaining, string.Empty);
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
            : Math.Max(0, Math.Max(0, _summary.TotalItemCount) - _completedItemCount);

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

    private string BuildConflictStatusText(string sourcePath)
    {
        if (_summary.ConflictFileCount <= 1)
        {
            string fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = sourcePath;

            return "The destination already has a file named '" + fileName + "'";
        }

        return "The destination already has " + _summary.ConflictFileCount.ToString("N0") + " items with the same name";
    }

    private void GetTransferErrorText(Exception exception, out string header, out string detail)
    {
        string operation = _move ? "move" : "copy";
        int errorCode = GetWin32ErrorCode(exception);

        switch (errorCode)
        {
            case 2:   // ERROR_FILE_NOT_FOUND
            case 3:   // ERROR_PATH_NOT_FOUND
                header = "The source item is no longer available.";
                detail = "It may have already been moved or deleted.";
                return;

            case 5:   // ERROR_ACCESS_DENIED
                header = $"You need permission to {operation} this item.";
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
                header = "The action can't be completed because the file is open in another program.";
                detail = "Close the file and try again.";
                return;

            case 39:  // ERROR_HANDLE_DISK_FULL
            case 112: // ERROR_DISK_FULL
                header = "There is not enough space on the destination.";
                detail = "Free up space and try again.";
                return;

            case 80:  // ERROR_FILE_EXISTS
            case 183: // ERROR_ALREADY_EXISTS
                header = "The destination already contains an item with the same name.";
                detail = "Choose a different name or skip this item.";
                return;

            case 123: // ERROR_INVALID_NAME
                header = "The item name is not valid.";
                detail = "Rename the item and try again.";
                return;

            case 206: // ERROR_FILENAME_EXCED_RANGE
                header = "The item path is too long.";
                detail = "Shorten the name or move it to a location with a shorter path, then try again.";
                return;
        }

        string message = StripQuotedPathsFromErrorMessage(exception.Message);
        header = "The action can't be completed.";
        detail = string.IsNullOrWhiteSpace(message)
            ? "Try again or skip this item."
            : message;
    }

    private static int GetWin32ErrorCode(Exception exception)
    {
        return exception switch
        {
            Win32Exception win32Exception => win32Exception.NativeErrorCode,
            UnauthorizedAccessException => 5,
            FileNotFoundException => 2,
            DirectoryNotFoundException => 3,
            PathTooLongException => 206,
            IOException => GetWin32CodeFromHResult(exception.HResult),
            _ => GetWin32CodeFromHResult(exception.HResult)
        };
    }

    private static int GetWin32CodeFromHResult(int hResult)
    {
        const int facilityWin32Mask = unchecked((int)0xFFFF0000);
        const int facilityWin32 = unchecked((int)0x80070000);

        if ((hResult & facilityWin32Mask) == facilityWin32)
            return hResult & 0xFFFF;

        return 0;
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

    private static string BuildTransferPathToolTip(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return string.IsNullOrWhiteSpace(sourcePath) ? string.Empty : "Source: " + sourcePath;

        if (string.IsNullOrWhiteSpace(sourcePath))
            return "Destination: " + destinationPath;

        return "Source: " + sourcePath + Environment.NewLine + "Destination: " + destinationPath;
    }

    private void ApplyErrorDetails(string path, bool isDirectory)
    {
        _errorDetailsActive = true;
        _errorDetailsIsDirectory = isDirectory;
        SetErrorDetailsVisible(true);

        string displayPath = path?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
        string fileName = string.IsNullOrWhiteSpace(displayPath) ? path ?? string.Empty : Path.GetFileName(displayPath) ?? string.Empty;

        _lblErrorName.Text = string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        _lblErrorName.Visible = true;

        if (isDirectory)
        {
            _lblErrorType.Visible = false;
            _lblErrorSize.Visible = false;

            _lblErrorModified.Text = "Date Created: " + FileOperationText.GetDateCreatedText(path);
            _lblErrorModified.Visible = true;
        }
        else
        {
            _lblErrorType.Text = "Type: " + GetFileTypeText(path);
            _lblErrorSize.Text = "Size: " + FileOperationText.GetSizeText(path);
            _lblErrorModified.Text = "Date Modified: " + FileOperationText.GetDateModifiedText(path);

            _lblErrorType.Visible = true;
            _lblErrorSize.Visible = true;
            _lblErrorModified.Visible = true;
        }

        SetErrorIcon(path, isDirectory);
    }

    private void SetErrorIcon(string path, bool isDirectory)
    {
        _errorIconPath = path ?? string.Empty;
        _errorIconIsDirectory = isDirectory;

        Image? oldImage = _picErrorIcon.Image;
        _picErrorIcon.Image = null;
        oldImage?.Dispose();

        int iconSize = _mPx.ErrorIconSize;
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

        _picErrorIcon.Image = image;
    }

    private void ClearErrorDetails()
    {
        SetErrorDetailsVisible(false);
        ClearErrorIcon();
    }

    private void ClearErrorIcon()
    {
        _errorIconPath = string.Empty;
        _errorIconIsDirectory = false;
        _errorDetailsActive = false;
        _errorDetailsIsDirectory = false;

        Image? oldImage = _picErrorIcon.Image;
        _picErrorIcon.Image = null;
        oldImage?.Dispose();
    }

    private void SetErrorDetailsVisible(bool visible)
    {
        _picErrorIcon.Visible = visible;
        _lblErrorName.Visible = visible;

        if (!visible)
        {
            _lblErrorType.Visible = false;
            _lblErrorSize.Visible = false;
            _lblErrorModified.Visible = false;
        }
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

    private void UpdateSummaryLineText()
    {
        long itemCount = Math.Max(0, _summary.TotalItemCount);
        string itemWord = itemCount == 1 ? "item" : "items";

        _lblSummaryPrefix.Text = _windowTitle + " " + itemCount.ToString("N0") + " " + itemWord + " from";

        SetPathLink(
            _lnkSourceFolder,
            GetFolderDisplayName(_summary.SourceFolderPath),
            _summary.SourceFolderPath);

        SetPathLink(
            _lnkDestinationFolder,
            GetFolderDisplayName(_summary.DestinationFolderPath),
            _summary.DestinationFolderPath);

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

    private LinkLabel CreatePathLinkLabel()
    {
        LinkLabel label = new()
        {
            AutoSize = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            LinkBehavior = LinkBehavior.HoverUnderline,
            TabStop = false,
            UseMnemonic = false
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

    private void BtnPrimary_Click(object? sender, EventArgs e)
    {
        switch (_mode)
        {
            case TransferWindowMode.Conflict:
                _applyConflictActionToAll = ExplorerTransferConflictAction.Overwrite;
                _pendingConflictDecision?.TrySetResult(new ExplorerTransferConflictDecision
                {
                    Action = ExplorerTransferConflictAction.Overwrite,
                    ApplyToAll = true
                });
                _pendingConflictDecision = null;
                ApplyProgressMode();
                break;

            case TransferWindowMode.Error:
                CompletePendingErrorAction(ExplorerTransferErrorAction.Retry);
                ApplyProgressMode();
                break;
        }
    }

    private void BtnSecondary_Click(object? sender, EventArgs e)
    {
        switch (_mode)
        {
            case TransferWindowMode.Conflict:
                _applyConflictActionToAll = ExplorerTransferConflictAction.Skip;
                _pendingConflictDecision?.TrySetResult(new ExplorerTransferConflictDecision
                {
                    Action = ExplorerTransferConflictAction.Skip,
                    ApplyToAll = true
                });
                _pendingConflictDecision = null;
                ApplyProgressMode();
                break;

            case TransferWindowMode.Error:
                CompletePendingErrorAction(ExplorerTransferErrorAction.Skip);
                ApplyProgressMode();
                break;
        }
    }

    private void CompletePendingErrorAction(ExplorerTransferErrorAction action)
    {
        if (_chkDoThisForAll.Visible && _chkDoThisForAll.Checked && action == ExplorerTransferErrorAction.Skip)
            _applyToAllErrorAction = action;

        _pendingErrorAction?.TrySetResult(action);
        _pendingErrorAction = null;
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        switch (_mode)
        {
            case TransferWindowMode.Progress:
                CancelOperation();
                break;

            case TransferWindowMode.Conflict:
                ShowCompareMode();
                break;

            case TransferWindowMode.Error:
                CompletePendingErrorAction(ExplorerTransferErrorAction.Cancel);
                CancelOperation();
                break;
        }
    }

    private void ShowCompareMode()
    {
        IReadOnlyList<ExplorerTransferConflictItem> conflictItems = _summary.ConflictItems;
        if (conflictItems.Count == 0 &&
            !string.IsNullOrWhiteSpace(_activeConflictSourcePath))
        {
            conflictItems =
            [
                new ExplorerTransferConflictItem(
                    _activeConflictSourcePath,
                    _activeConflictDestinationPath)
            ];
        }

        DisposeCompareView();

        int compareClientWidth = ClientSize.Width;

        TransferConflictCompareView compareView = new(
            _fileAssociations,
            conflictItems,
            _summary.SourceFolderPath,
            _summary.DestinationFolderPath,
            _openFolderInNewWindow,
            compareClientWidth,
            _currentDpi);

        Size compareClientSize = compareView.ClientSize;

        compareView.Dock = DockStyle.Fill;

        _compareView = compareView;
        _mode = TransferWindowMode.Compare;
        Text = compareView.Text;

        compareView.CancelClicked += CompareMode_CancelClicked;
        compareView.ContinueClicked += CompareMode_ContinueClicked;

        SuspendLayout();

        try
        {
            SetMainLayoutVisible(false);
            ClientSize = compareClientSize;
            Controls.Add(compareView);
            compareView.BringToFront();
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }

        compareView.FocusContinueButton();
    }

    private void CompareMode_CancelClicked(object? sender, EventArgs e)
    {
        DisposeCompareView();
        RestoreConflictMode();
    }

    private void CompareMode_ContinueClicked(object? sender, EventArgs e)
    {
        TransferConflictCompareView? compareView = _compareView;
        if (compareView == null)
            return;

        _compareConflictActions = compareView.SelectedActions.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);

        ExplorerTransferConflictAction activeAction =
            _compareConflictActions.TryGetValue(_activeConflictSourcePath, out ExplorerTransferConflictAction action)
                ? action
                : ExplorerTransferConflictAction.Skip;

        DisposeCompareView();
        ApplyProgressMode();

        _pendingConflictDecision?.TrySetResult(new ExplorerTransferConflictDecision
        {
            Action = activeAction,
            ApplyToAll = true
        });

        _pendingConflictDecision = null;
    }

    private void DisposeCompareView()
    {
        TransferConflictCompareView? compareView = _compareView;
        if (compareView == null)
            return;

        _compareView = null;
        compareView.CancelClicked -= CompareMode_CancelClicked;
        compareView.ContinueClicked -= CompareMode_ContinueClicked;
        Controls.Remove(compareView);
        compareView.Dispose();
    }

    private void ApplyCompareDpiLayout()
    {
        TransferConflictCompareView? compareView = _compareView;
        if (compareView == null)
            return;

        Size compareClientSize = compareView.ApplyDpiLayout(_currentDpi, _mPx.ClientWidth);

        if (ClientSize != compareClientSize)
            ClientSize = compareClientSize;
    }

    private void RestoreConflictMode()
    {
        _mode = TransferWindowMode.Conflict;
        Text = _windowTitle;
        SetMainLayoutVisible(true);
        ApplyLayoutMetrics();
        Activate();
    }

    private void SetMainLayoutVisible(bool visible)
    {
        _summaryPanel.Visible = visible && _mode != TransferWindowMode.Error;
        _lblOperation.Visible = visible && (_mode == TransferWindowMode.Conflict || _mode == TransferWindowMode.Error);
        _lblDetail.Visible = visible && _mode == TransferWindowMode.Error;
        SetErrorDetailsVisible(visible && _mode == TransferWindowMode.Error && _errorDetailsActive);
        _chkDoThisForAll.Visible = visible &&
            _mode == TransferWindowMode.Error &&
            _errorSkipVisible &&
            !_summary.IsSingleTopLevelFile;
        _lblStatus.Visible = visible && _mode == TransferWindowMode.Progress;
        _progressBar.Visible = visible && _mode == TransferWindowMode.Progress;
        _lblCurrentName.Visible = visible && _mode == TransferWindowMode.Progress;
        _lblItemsRemaining.Visible = visible && _mode == TransferWindowMode.Progress;
        _btnPrimary.Visible = visible && _btnPrimary.Text.Length != 0 && _mode != TransferWindowMode.Progress;
        _btnSecondary.Visible = visible && _btnSecondary.Text.Length != 0 && _mode != TransferWindowMode.Progress &&
            !(_mode == TransferWindowMode.Error && !_errorSkipVisible);
        _btnCancel.Visible = visible && _mode != TransferWindowMode.Compare;
    }

    private void TransferProgressForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        StopShowDelayTimer();
        DisposeCompareView();

        if (e.CloseReason == CloseReason.UserClosing)
        {
            _pendingConflictDecision?.TrySetResult(new ExplorerTransferConflictDecision
            {
                Action = ExplorerTransferConflictAction.Cancel,
                ApplyToAll = false
            });

            _pendingErrorAction?.TrySetResult(ExplorerTransferErrorAction.Cancel);
            _pendingConflictDecision = null;
            _pendingErrorAction = null;

            _isCancelled = true;
            ClearErrorDetails();
        }
    }

    private void CancelOperation()
    {
        _isCancelled = true;
        _btnCancel.Enabled = false;
        _btnCancel.Text = "Cancelling...";
    }

    private void EnsureShownCore()
    {
        StopShowDelayTimer();

        ShellDialogChrome.ShowCenteredNonModalUnowned(this, _referenceOwnerForm);
    }

    private void StopShowDelayTimer()
    {
        if (_showDelayTimer == null)
            return;

        try { _showDelayTimer.Stop(); } catch { }
        try { _showDelayTimer.Dispose(); } catch { }
        _showDelayTimer = null;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ReapplyDpiLayout(shouldCenter: false);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ReapplyDpiLayout(shouldCenter: true);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ReapplyDpiLayout(e.DeviceDpiNew, shouldCenter: true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (_mode != TransferWindowMode.Compare && !_updatingMainLayout)
            ApplyLayoutMetrics();
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

    private void ReapplyDpiLayout(int dpi = 0, bool shouldCenter = false)
    {
        CaptureCurrentDpi(dpi);
        ReapplyDpiMetrics(updateLayout: _mode != TransferWindowMode.Compare);
        RefreshErrorIcon();

        if (_mode == TransferWindowMode.Compare)
            ApplyCompareDpiLayout();

        if (shouldCenter)
            ShellDialogChrome.CenterOnOwnerScreen(this, _referenceOwnerForm);
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
        _mPx = TransferLayoutMetricsPx.FromDip(
            _mDip,
            ScaleDip,
            Font,
            _headerFont ?? Font);

        ApplyWrappedLabelMetrics();
        ApplyButtonWidths();
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

            if (!ReferenceEquals(_lblSummaryMiddle.Font, _bodyFont))
                _lblSummaryMiddle.Font = _bodyFont;

            if (!ReferenceEquals(_lnkDestinationFolder.Font, _bodyFont))
                _lnkDestinationFolder.Font = _bodyFont;
        }
    }

    private void ApplyLayoutMetrics()
    {
        if (_mode == TransferWindowMode.Compare || _updatingMainLayout)
            return;

        _updatingMainLayout = true;
        SuspendLayout();

        try
        {
            Size clientSize = new(_mPx.ClientWidth, GetClientHeightForCurrentMode());
            if (ClientSize != clientSize)
                ClientSize = clientSize;

            int summaryHeight = GetSummaryHeight();
            int operationHeight = GetOperationHeightForCurrentMode();
            int detailHeight = GetDetailHeightForCurrentMode();

            SetBoundsIfChanged(
                _summaryPanel,
                _mPx.Margin,
                _mPx.HeaderTop,
                _mPx.ContentWidth,
                summaryHeight);
            LayoutSummaryControls();

            SetBoundsIfChanged(
                _lblOperation,
                _mPx.Margin,
                GetOperationTopForCurrentMode(),
                _mPx.ContentWidth,
                operationHeight);

            SetBoundsIfChanged(
                _lblDetail,
                _mPx.Margin,
                GetDetailTopForCurrentMode(),
                _mPx.ContentWidth,
                detailHeight);

            SetBoundsIfChanged(
                _picErrorIcon,
                _mPx.Margin,
                GetErrorDetailsTopForCurrentMode(),
                _mPx.ErrorIconSize,
                _mPx.ErrorIconSize);

            LayoutErrorDetailRows();

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
            _updatingMainLayout = false;
        }
    }

    private void LayoutSummaryControls()
    {
        int x = 0;

        LayoutSummaryText(_lblSummaryPrefix, ref x);
        LayoutSummaryLink(_lnkSourceFolder, ref x);
        LayoutSummaryText(_lblSummaryMiddle, ref x);
        LayoutSummaryLink(_lnkDestinationFolder, ref x);

        int bottom = Math.Max(
            Math.Max(_lblSummaryPrefix.Bottom, _lnkSourceFolder.Bottom),
            Math.Max(_lblSummaryMiddle.Bottom, _lnkDestinationFolder.Bottom));

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

    private void LayoutErrorDetailRows()
    {
        int infoTop = GetErrorDetailsTopForCurrentMode();
        int textLeft = _mPx.ErrorTextLeft;
        int textWidth = _mPx.ErrorTextWidth;
        int labelHeight = _mPx.ErrorLineHeight;
        int rowStep = _mPx.ErrorRowStep;

        SetBoundsIfChanged(
            _lblErrorName,
            textLeft,
            infoTop,
            textWidth,
            labelHeight);

        SetBoundsIfChanged(
            _lblErrorType,
            textLeft,
            infoTop + rowStep,
            textWidth,
            labelHeight);

        SetBoundsIfChanged(
            _lblErrorSize,
            textLeft,
            infoTop + (rowStep * 2),
            textWidth,
            labelHeight);

        int modifiedRow = _errorDetailsActive && !_errorDetailsIsDirectory ? 3 : 1;
        SetBoundsIfChanged(
            _lblErrorModified,
            textLeft,
            infoTop + (rowStep * modifiedRow),
            textWidth,
            labelHeight);
    }

    private void LayoutButtonsForCurrentMode()
    {
        int buttonTop = GetButtonTopForCurrentMode();

        if (_mode == TransferWindowMode.Progress)
        {
            SetButtonBounds(_btnCancel, GetRightAlignedButtonLeft([_btnCancel], 0), buttonTop);
            return;
        }

        Button[] buttons = _mode == TransferWindowMode.Error && !_errorSkipVisible
            ? [_btnPrimary, _btnCancel]
            : [_btnPrimary, _btnSecondary, _btnCancel];

        for (int i = 0; i < buttons.Length; i++)
            SetButtonBounds(buttons[i], GetRightAlignedButtonLeft(buttons, i), buttonTop);
    }

    private int GetSummaryHeight()
    {
        return Math.Max(_mPx.HeaderLineHeight, _summaryPanel.Height);
    }

    private int GetClientHeightForCurrentMode()
    {
        return GetButtonTopForCurrentMode() + _mPx.ButtonHeight + _mPx.Margin;
    }

    private int GetButtonTopForCurrentMode()
    {
        int gap = _mode == TransferWindowMode.Error && _chkDoThisForAll.Visible
            ? _mPx.CheckBoxToButtonsGap
            : _mPx.ButtonVerticalGap;

        return GetContentBlockBottomForCurrentMode() + gap;
    }

    private int GetContentBlockBottomForCurrentMode()
    {
        if (_mode == TransferWindowMode.Error)
        {
            if (_chkDoThisForAll.Visible)
                return GetErrorCheckBoxTopForCurrentMode() + _mPx.CheckBoxHeight;

            if (_errorDetailsActive)
                return GetErrorDetailsTopForCurrentMode() + _mPx.ErrorBlockHeight;

            return GetDetailTopForCurrentMode() + GetDetailHeightForCurrentMode();
        }

        if (_mode == TransferWindowMode.Conflict)
            return GetOperationTopForCurrentMode() + GetOperationHeightForCurrentMode();

        return _mPx.ProgressItemsTop + _mPx.BodyLineHeight;
    }

    private int GetOperationTopForCurrentMode()
    {
        if (_mode == TransferWindowMode.Error)
            return _mPx.HeaderTop;

        return _mPx.HeaderTop + _mPx.HeaderLineHeight + _mPx.SummaryToBodyGap;
    }

    private int GetDetailTopForCurrentMode()
    {
        int gap = _mode == TransferWindowMode.Error
            ? _mPx.HeaderToBodyGap
            : _mPx.OperationToDetailGap;

        return GetOperationTopForCurrentMode() + GetOperationHeightForCurrentMode() + gap;
    }

    private int GetErrorDetailsTopForCurrentMode()
    {
        return GetDetailTopForCurrentMode() + GetDetailHeightForCurrentMode() + _mPx.ErrorDetailToDetailsGap;
    }

    private int GetErrorCheckBoxTopForCurrentMode()
    {
        int previousBottom = _mode == TransferWindowMode.Error && _errorDetailsActive
            ? GetErrorDetailsTopForCurrentMode() + _mPx.ErrorBlockHeight
            : GetDetailTopForCurrentMode() + GetDetailHeightForCurrentMode();

        return previousBottom + _mPx.ButtonVerticalGap;
    }

    private int GetOperationHeightForCurrentMode()
    {
        if ((_mode != TransferWindowMode.Conflict && _mode != TransferWindowMode.Error) || !_lblOperation.Visible)
            return _mPx.HeaderLineHeight;

        return MeasureWrappedLabelHeight(
            _lblOperation,
            _mPx.ContentWidth,
            _mPx.HeaderLineHeight);
    }

    private int GetDetailHeightForCurrentMode()
    {
        if ((_mode != TransferWindowMode.Conflict && _mode != TransferWindowMode.Error) || !_lblDetail.Visible)
            return _mPx.BodyLineHeight;

        return MeasureWrappedLabelHeight(
            _lblDetail,
            _mPx.ContentWidth,
            _mPx.BodyLineHeight);
    }

    private void SetButtonWidths(int primaryDip, int secondaryDip, int cancelDip)
    {
        _primaryButtonWidthDip = primaryDip;
        _secondaryButtonWidthDip = secondaryDip;
        _cancelButtonWidthDip = cancelDip;
        ApplyButtonWidths();
    }

    private void ApplyButtonWidths()
    {
        SetButtonSize(_btnPrimary, _primaryButtonWidthDip);
        SetButtonSize(_btnSecondary, _secondaryButtonWidthDip);
        SetButtonSize(_btnCancel, _cancelButtonWidthDip);
    }

    private void SetButtonSize(Button button, int widthDip)
    {
        int width = Math.Max(_mPx.ButtonWidth, ScaleDip(widthDip));
        if (button.Width != width || button.Height != _mPx.ButtonHeight)
            button.Size = new Size(width, _mPx.ButtonHeight);
    }

    private void SetButtonBounds(Button button, int left, int top)
    {
        SetBoundsIfChanged(
            button,
            left,
            top,
            button.Width,
            _mPx.ButtonHeight);
    }

    private int GetRightAlignedButtonLeft(IReadOnlyList<Button> buttons, int index)
    {
        int totalWidth = 0;
        foreach (Button button in buttons)
            totalWidth += button.Width;

        totalWidth += Math.Max(0, buttons.Count - 1) * _mPx.ButtonGap;

        int left = _mPx.ClientWidth - _mPx.ButtonRightMargin - totalWidth;
        for (int i = 0; i < index; i++)
            left += buttons[i].Width + _mPx.ButtonGap;

        return left;
    }

    private void RefreshErrorIcon()
    {
        if (!_errorDetailsActive || string.IsNullOrWhiteSpace(_errorIconPath))
            return;

        SetErrorIcon(_errorIconPath, _errorIconIsDirectory);
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

    private bool RunOnUiThread(Action action)
    {
        if (IsDisposed || Disposing)
            return false;

        try
        {
            if (IsHandleCreated)
            {
                if (InvokeRequired)
                {
                    Invoke(action);
                    return true;
                }

                action();
                return true;
            }

            if (_uiContext != null && SynchronizationContext.Current != _uiContext)
            {
                bool executed = false;

                _uiContext.Send(_ =>
                {
                    if (IsDisposed || Disposing)
                        return;

                    action();
                    executed = true;
                }, null);

                return executed;
            }

            action();
            return true;
        }
        catch
        {
            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        Font? bodyFont = null;
        Font? headerFont = null;

        if (disposing)
        {
            StopShowDelayTimer();
            DisposeCompareView();
            ClearErrorDetails();
            _pathToolTip.Dispose();

            bodyFont = _bodyFont;
            _bodyFont = null;

            headerFont = _headerFont;
            _headerFont = null;
        }

        base.Dispose(disposing);

        bodyFont?.Dispose();
        headerFont?.Dispose();
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
    private sealed class TransferLayoutMetrics
    {
        public int ClientWidthDip { get; init; } = 450;

        public int MarginDip { get; init; } = 12;
        public int RightMarginDip { get; init; } = 28;

        public int HeaderTopDip { get; init; } = 6;
        public int HeaderLineHeightDip { get; init; } = 22;
        public int HeaderToBodyGapDip { get; init; } = 12;
        public int SummaryToBodyGapDip { get; init; } = 6;
        public int OperationToDetailGapDip { get; init; } = 6;
        public int ErrorDetailToDetailsGapDip { get; init; } = 0;

        public int BodyLineHeightDip { get; init; } = 18;
        public int FontHeightPaddingDip { get; init; } = 4;
        public int WrappedLineStepReductionDip { get; init; } = 6;

        public int ErrorIconTextGapDip { get; init; } = 16;
        public int ErrorLineHeightDip { get; init; } = 16;
        public int ErrorFontHeightPaddingDip { get; init; } = 1;
        public int ErrorRowCount { get; init; } = 4;

        public int CheckBoxLeftNudgeDip { get; init; } = 6;
        public int CheckBoxToButtonsGapDip { get; init; } = 2;

        public int ProgressBodyRowGapDip { get; init; } = 8;
        public int ProgressStatusToBarGapDip { get; init; } = 0;
        public int ProgressBarHeightDip { get; init; } = 22;

        public int ButtonVerticalGapDip { get; init; } = 12;
        public int ButtonWidthDip { get; init; } = 80;
        public int ButtonHeightDip { get; init; } = 25;
        public int ButtonGapDip { get; init; } = 10;
        public float HeaderFontSizePt { get; init; } = 10.5f;
    }

    private sealed class TransferLayoutMetricsPx
    {
        public int ClientWidth { get; init; }
        public int Margin { get; init; }
        public int RightMargin { get; init; }
        public int ContentWidth { get; init; }

        public int HeaderTop { get; init; }
        public int HeaderLineHeight { get; init; }
        public int HeaderToBodyGap { get; init; }
        public int SummaryToBodyGap { get; init; }
        public int OperationToDetailGap { get; init; }
        public int BodyLineHeight { get; init; }
        public int WrappedLineStepReduction { get; init; }
        public int SummaryTextTopMargin { get; init; }
        public int SummaryLinkTopMargin { get; init; }
        public int SummaryInlineGap { get; init; }

        public int ErrorIconSize { get; init; }
        public int ErrorLineHeight { get; init; }
        public int ErrorRowStep { get; init; }
        public int ErrorBlockHeight { get; init; }
        public int ErrorTextLeft { get; init; }
        public int ErrorTextWidth { get; init; }
        public int ErrorDetailToDetailsGap { get; init; }
        public int CheckBoxHeight { get; init; }
        public int CheckBoxLeftNudge { get; init; }
        public int CheckBoxToButtonsGap { get; init; }

        public int ProgressStatusTop { get; init; }
        public int ProgressBarTop { get; init; }
        public int ProgressNameTop { get; init; }
        public int ProgressItemsTop { get; init; }
        public int ProgressBarHeight { get; init; }

        public int ButtonVerticalGap { get; init; }
        public int ButtonWidth { get; init; }
        public int ButtonHeight { get; init; }
        public int ButtonGap { get; init; }
        public int ButtonRightMargin { get; init; }

        public static TransferLayoutMetricsPx FromDip(
            TransferLayoutMetrics dip,
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
            int errorLineHeight = Math.Max(
                scale(dip.ErrorLineHeightDip),
                bodyFont.Height + scale(dip.ErrorFontHeightPaddingDip));

            int errorRowStep = GetTightRowStep(
                errorLineHeight,
                bodyFont,
                scale(2));

            int errorBlockHeight = errorLineHeight + (errorRowStep * (dip.ErrorRowCount - 1));
            int errorIconSize = errorBlockHeight;
            int errorTextLeft = margin + errorIconSize + scale(dip.ErrorIconTextGapDip);
            int errorDetailToDetailsGap = scale(dip.ErrorDetailToDetailsGapDip);

            int progressStatusToBarGap = scale(dip.ProgressStatusToBarGapDip);
            int progressBodyRowGap = scale(dip.ProgressBodyRowGapDip);
            int progressTextRowStep = GetTightRowStep(
                bodyLineHeight,
                bodyFont,
                scale(2));

            int headerToBodyGap = scale(dip.HeaderToBodyGapDip);
            int summaryToBodyGap = scale(dip.SummaryToBodyGapDip);
            int progressStatusTop = headerTop + headerLineHeight + summaryToBodyGap;
            int progressBarHeight = scale(dip.ProgressBarHeightDip);
            int progressBarTop = progressStatusTop + bodyLineHeight + progressStatusToBarGap;
            int progressNameTop = progressBarTop + progressBarHeight + progressBodyRowGap;
            int progressItemsTop = progressNameTop + progressTextRowStep;
            int checkBoxHeight = bodyLineHeight;

            int buttonHeight = Math.Max(
                scale(dip.ButtonHeightDip),
                bodyFont.Height + scale(10));

            return new TransferLayoutMetricsPx
            {
                ClientWidth = clientWidth,

                Margin = margin,
                RightMargin = rightMargin,
                ContentWidth = clientWidth - margin - rightMargin,

                HeaderTop = headerTop,
                HeaderLineHeight = headerLineHeight,
                HeaderToBodyGap = headerToBodyGap,
                SummaryToBodyGap = summaryToBodyGap,
                OperationToDetailGap = scale(dip.OperationToDetailGapDip),
                BodyLineHeight = bodyLineHeight,
                WrappedLineStepReduction = scale(dip.WrappedLineStepReductionDip),
                SummaryTextTopMargin = scale(SummaryTextTopMarginDip),
                SummaryLinkTopMargin = scale(SummaryLinkTopMarginDip),
                SummaryInlineGap = scale(SummaryInlineGapDip),

                ErrorIconSize = errorIconSize,
                ErrorLineHeight = errorLineHeight,
                ErrorRowStep = errorRowStep,
                ErrorBlockHeight = errorBlockHeight,
                ErrorTextLeft = errorTextLeft,
                ErrorTextWidth = clientWidth - errorTextLeft - rightMargin,
                ErrorDetailToDetailsGap = errorDetailToDetailsGap,
                CheckBoxHeight = checkBoxHeight,
                CheckBoxLeftNudge = scale(dip.CheckBoxLeftNudgeDip),
                CheckBoxToButtonsGap = scale(dip.CheckBoxToButtonsGapDip),

                ProgressStatusTop = progressStatusTop,
                ProgressBarTop = progressBarTop,
                ProgressNameTop = progressNameTop,
                ProgressItemsTop = progressItemsTop,
                ProgressBarHeight = progressBarHeight,

                ButtonVerticalGap = scale(dip.ButtonVerticalGapDip),
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
