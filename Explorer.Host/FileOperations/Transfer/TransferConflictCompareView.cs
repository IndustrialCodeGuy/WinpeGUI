using Shared.Shell.Interop;
using Shared.Shell.Utilities;
using Shell.Core.FileTypes;
using UiExplorerIconCache = Explorer.UI.Icons.ExplorerIconCache;

namespace Explorer.Host.FileOperations.Transfer;

internal sealed class TransferConflictCompareView : UserControl
{
    private const int DefaultDpi = 96;
    private const int DefaultCompareViewWidthDip = 560;
    private const int WmSetRedraw = 0x000B;
    private const int ContentMarginDip = ShellDialogChrome.CompactContentMargin;
    private const int BodyRowHeightDip = 92;
    private const int MaxVisibleBodyRows = 3;
    private const int CompareFooterHeightDip = 46;
    private const int FooterSeparatorGapDip = ShellDialogChrome.FooterGap;
    private const int CompareHeaderHeightDip = 100;
    private const int HeaderCheckBoxWidthDip = 18;
    private const int HeaderTextGapDip = -3;
    private const int HeaderLinkTopOffsetDip = 1;
    private const int ButtonWidthDip = ShellDialogChrome.ButtonWidth;
    private const int ButtonGapDip = ShellDialogChrome.ButtonGap;
    private const int ButtonHeightDip = ShellDialogChrome.ButtonHeight;
    private const int ColumnGapDip = 8;
    private const int HeaderQuestionTopDip = 10;
    private const int HeaderDetailTopDip = 34;
    private const int ColumnHeaderTopDip = 64;
    private const int ColumnHeaderHeightDip = 32;
    private const int HeaderCheckTopDip = 6;
    private const int EmptyMessageTopDip = 10;
    private const int BodyLineHeightDip = ShellDialogChrome.BodyLineHeight;
    private const int HeaderLineHeightDip = ShellDialogChrome.HeaderLineHeight;
    private const int FontHeightPaddingDip = 4;
    private const float HeaderFontSizePt = 10.5f;

    private readonly Panel _columnHeaderSeparator;
    private int _viewWidth;
    private int _currentDpi = DefaultDpi;
    private CompareLayoutMetricsPx _mPx = CompareLayoutMetricsPx.Empty;
    private Font? _bodyFont;
    private Font? _headerFont;
    private float _lastBodyFontSizePx;
    private float _lastHeaderFontSizePx;
    private readonly Label _lblSourceAllPrefix;
    private readonly Label _lblDestinationAllPrefix;
    private readonly Panel _footerSeparator;
    private readonly IReadOnlyList<ExplorerTransferConflictItem> _conflictItems;
    private readonly List<ConflictRowPanel> _rows = [];
    private readonly Panel _headerPanel;
    private readonly Panel _bodyPanel;
    private readonly FlowLayoutPanel _listPanel;
    private readonly Panel _columnHeaderPanel;
    private readonly Panel _footerPanel;
    private readonly Label _lblQuestion;
    private readonly Label _lblDetail;
    private readonly CheckBox _chkSourceAll;
    private readonly CheckBox _chkDestinationAll;
    private readonly Button _btnContinue;
    private readonly Button _btnCancel;
    private readonly Action<string>? _openFolderInNewWindow;
    private readonly ToolTip _pathToolTip = new();
    private readonly LinkLabel _lnkSourceFolder;
    private readonly LinkLabel _lnkDestinationFolder;
    private readonly CheckBox _chkSkipSameDateSize;
    private readonly Label _lblEmptyMessage;
    private readonly int _sameDateAndSizeCount;
    private bool _updatingHeaderChecks;
    private bool _updatingRows;
    private bool _applyingDpiLayout;

    public event EventHandler? ContinueClicked;
    public event EventHandler? CancelClicked;

    public TransferConflictCompareView(
        IExplorerFileAssociationService fileAssociations,
        IReadOnlyList<ExplorerTransferConflictItem> conflictItems,
        string sourceFolderPath,
        string destinationFolderPath,
        Action<string>? openFolderInNewWindow = null,
        int preferredClientWidth = 0,
        int currentDpi = 0)
    {
        ArgumentNullException.ThrowIfNull(fileAssociations);

        CaptureCurrentDpi(currentDpi);
        _viewWidth = preferredClientWidth > 0
            ? preferredClientWidth
            : ScaleDip(DefaultCompareViewWidthDip);

        _conflictItems = conflictItems ?? Array.Empty<ExplorerTransferConflictItem>();
        _openFolderInNewWindow = openFolderInNewWindow;
        _sameDateAndSizeCount = _conflictItems.Count(static item =>
            IsSameDateAndSize(item.SourcePath, item.DestinationPath));
        Text = BuildTitle(_conflictItems.Count);
        AutoScaleMode = AutoScaleMode.None;
        Margin = Padding.Empty;
        Padding = Padding.Empty;

        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Padding = Padding.Empty
        };

        _lblQuestion = new Label
        {
            Text = "Which files do you want to keep?",
            AutoEllipsis = true,
            UseMnemonic = false
        };

        _lblDetail = new Label
        {
            Text = "If you select both versions, the copied file will have a number added to its name.",
            AutoEllipsis = true,
            UseMnemonic = false
        };

        _headerPanel.Controls.Add(_lblQuestion);
        _headerPanel.Controls.Add(_lblDetail);

        _footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Padding = Padding.Empty
        };

        _footerSeparator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = SystemColors.ControlDark
        };

        _chkSkipSameDateSize = new CheckBox
        {
            Text = BuildSkipSameDateSizeText(_sameDateAndSizeCount),
            Enabled = _sameDateAndSizeCount > 0,
            AutoEllipsis = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        _chkSkipSameDateSize.CheckedChanged += (_, _) => ApplyConflictFilter();

        _btnCancel = new Button
        {
            Text = "Cancel",
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        _btnCancel.Click += (_, _) => CancelClicked?.Invoke(this, EventArgs.Empty);

        _btnContinue = new Button
        {
            Text = "Continue",
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        _btnContinue.Click += (_, _) => ContinueClicked?.Invoke(this, EventArgs.Empty);

        _footerPanel.Controls.Add(_footerSeparator);
        _footerPanel.Controls.Add(_chkSkipSameDateSize);
        _footerPanel.Controls.Add(_btnContinue);
        _footerPanel.Controls.Add(_btnCancel);
        _footerPanel.Resize += (_, _) => LayoutFooterButtons();

        _bodyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = Padding.Empty
        };

        _listPanel = new FlowLayoutPanel
        {
            Left = 0,
            Top = 0,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _lblEmptyMessage = new Label
        {
            Left = 0,
            Text = "All files have the same date and size",
            TextAlign = ContentAlignment.TopCenter,
            Visible = false,
            UseMnemonic = false
        };

        _columnHeaderPanel = new Panel
        {
            Left = 0,
            Margin = Padding.Empty
        };

        _columnHeaderSeparator = new Panel
        {
            Left = 0,
            Height = 1,
            BackColor = SystemColors.ControlDark
        };

        _chkSourceAll = new CheckBox
        {
            Left = 0,
            Text = string.Empty,
            AutoSize = false
        };
        _chkSourceAll.CheckedChanged += ChkSourceAll_CheckedChanged;

        _lblSourceAllPrefix = CreateHeaderPrefixLabel("Files from");

        _lnkSourceFolder = CreatePathLinkLabel();
        SetPathLink(
            _lnkSourceFolder,
            GetFolderDisplayName(sourceFolderPath),
            sourceFolderPath);

        _chkDestinationAll = new CheckBox
        {
            Text = string.Empty,
            AutoSize = false
        };
        _chkDestinationAll.CheckedChanged += ChkDestinationAll_CheckedChanged;

        _lblDestinationAllPrefix = CreateHeaderPrefixLabel("Files already in");

        _lnkDestinationFolder = CreatePathLinkLabel();
        SetPathLink(
            _lnkDestinationFolder,
            GetFolderDisplayName(destinationFolderPath),
            destinationFolderPath);

        _columnHeaderPanel.Controls.Add(_chkSourceAll);
        _columnHeaderPanel.Controls.Add(_lblSourceAllPrefix);
        _columnHeaderPanel.Controls.Add(_lnkSourceFolder);

        _columnHeaderPanel.Controls.Add(_chkDestinationAll);
        _columnHeaderPanel.Controls.Add(_lblDestinationAllPrefix);
        _columnHeaderPanel.Controls.Add(_lnkDestinationFolder);

        _columnHeaderPanel.Controls.Add(_columnHeaderSeparator);
        _headerPanel.Controls.Add(_columnHeaderPanel);

        for (int i = 0; i < _conflictItems.Count; i++)
        {
            ExplorerTransferConflictItem item = _conflictItems[i];

            ConflictRowPanel row = new(
                fileAssociations,
                item,
                IsSameDateAndSize(item.SourcePath, item.DestinationPath));

            row.SelectionChanged += (_, _) =>
            {
                if (!_updatingRows)
                    UpdateHeaderChecks();
            };
            _rows.Add(row);
            _listPanel.Controls.Add(row);
        }

        _bodyPanel.Controls.Add(_listPanel);
        _bodyPanel.Controls.Add(_lblEmptyMessage);
        _lblEmptyMessage.BringToFront();

        Controls.Add(_bodyPanel);
        Controls.Add(_footerPanel);
        Controls.Add(_headerPanel);

        _bodyPanel.Resize += (_, _) => LayoutBodyContent();
        Load += (_, _) =>
        {
            ApplyDpiLayout(_currentDpi, _viewWidth);
            ApplyConflictFilter();
        };

        ApplyDpiLayout(_currentDpi, _viewWidth);
    }

    public IReadOnlyDictionary<string, ExplorerTransferConflictAction> SelectedActions => _rows
        .ToDictionary(
            static row => row.SourcePath,
            static row => row.IncludedInList
                ? row.Action
                : ExplorerTransferConflictAction.Skip,
            StringComparer.OrdinalIgnoreCase);

    public Size ApplyDpiLayout(int dpi = 0, int preferredClientWidth = 0)
    {
        if (preferredClientWidth > 0)
            _viewWidth = preferredClientWidth;

        CaptureCurrentDpi(dpi);
        RebuildFonts();
        RecalcMetrics();
        ApplyLayoutMetrics();

        return ClientSize;
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

    private void RebuildFonts()
    {
        Font baseFont = ShellDialogChrome.DialogFont;
        string familyName = baseFont.FontFamily.Name;

        float bodyFontSizePx = ScaleFontPointToPx(baseFont.SizeInPoints);
        float headerFontSizePx = ScaleFontPointToPx(HeaderFontSizePt);

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

        if (_bodyFont != null && !ReferenceEquals(Font, _bodyFont))
            Font = _bodyFont;

        if (_headerFont != null && !ReferenceEquals(_lblQuestion.Font, _headerFont))
            _lblQuestion.Font = _headerFont;

        if (_bodyFont != null && !ReferenceEquals(_lblDetail.Font, _bodyFont))
            _lblDetail.Font = _bodyFont;

        oldBodyFont?.Dispose();
        oldHeaderFont?.Dispose();
    }

    private void RecalcMetrics()
    {
        _mPx = CompareLayoutMetricsPx.FromDip(
            Math.Max(ScaleDip(300), _viewWidth),
            ScaleDip,
            Font,
            _headerFont ?? Font);
    }

    private void ApplyLayoutMetrics()
    {
        if (_applyingDpiLayout)
            return;

        _applyingDpiLayout = true;
        SuspendLayout();
        _headerPanel.SuspendLayout();
        _bodyPanel.SuspendLayout();
        _footerPanel.SuspendLayout();
        _columnHeaderPanel.SuspendLayout();
        _listPanel.SuspendLayout();

        try
        {
            Size clientSize = new(_mPx.ViewWidth, _mPx.ClientHeight);
            if (ClientSize != clientSize)
                ClientSize = clientSize;

            _headerPanel.Height = _mPx.HeaderHeight;
            _headerPanel.Padding = new Padding(
                _mPx.ContentMargin,
                _mPx.HeaderQuestionTop,
                _mPx.ContentMargin,
                _mPx.HeaderBottomPadding);

            SetBoundsIfChanged(
                _lblQuestion,
                _mPx.ContentMargin,
                _mPx.HeaderQuestionTop,
                Math.Max(20, _mPx.ViewWidth - (_mPx.ContentMargin * 2) - ScaleDip(12)),
                _mPx.HeaderLineHeight);

            SetBoundsIfChanged(
                _lblDetail,
                _mPx.ContentMargin,
                _mPx.HeaderDetailTop,
                Math.Max(20, _mPx.ViewWidth - (_mPx.ContentMargin * 2) - ScaleDip(12)),
                _mPx.BodyLineHeight);

            SetBoundsIfChanged(
                _columnHeaderPanel,
                0,
                _mPx.ColumnHeaderTop,
                _mPx.ViewWidth,
                _mPx.ColumnHeaderHeight);

            _columnHeaderSeparator.Top = Math.Max(0, _mPx.ColumnHeaderHeight - 1);
            _columnHeaderSeparator.Height = 1;
            _columnHeaderSeparator.Width = _columnHeaderPanel.Width;

            _footerPanel.Height = _mPx.FooterHeight;
            _footerSeparator.Height = 1;

            _chkSkipSameDateSize.Height = _mPx.BodyLineHeight;
            _btnCancel.Width = _mPx.ButtonWidth;
            _btnCancel.Height = _mPx.ButtonHeight;
            _btnContinue.Width = _mPx.ButtonWidth;
            _btnContinue.Height = _mPx.ButtonHeight;

            _lblEmptyMessage.Height = _mPx.BodyLineHeight;

            foreach (ConflictRowPanel row in _rows)
                row.ApplyDpi(_mPx);

            LayoutFooterButtons();
            LayoutBodyContent();
        }
        finally
        {
            _listPanel.ResumeLayout(performLayout: true);
            _columnHeaderPanel.ResumeLayout(performLayout: true);
            _footerPanel.ResumeLayout(performLayout: true);
            _bodyPanel.ResumeLayout(performLayout: true);
            _headerPanel.ResumeLayout(performLayout: true);
            ResumeLayout(performLayout: true);
            _applyingDpiLayout = false;
        }
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

    private void ChkSourceAll_CheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingHeaderChecks)
            return;

        SetAllRows(sourceColumn: true, isChecked: _chkSourceAll.Checked);
    }

    private void ChkDestinationAll_CheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingHeaderChecks)
            return;

        SetAllRows(sourceColumn: false, isChecked: _chkDestinationAll.Checked);
    }

    private void SetAllRows(bool sourceColumn, bool isChecked)
    {
        SuspendConflictListUpdates();
        _updatingRows = true;

        try
        {
            foreach (ConflictRowPanel row in _rows)
            {
                if (row.IncludedInList)
                    row.SetColumnChecked(sourceColumn, isChecked);
            }
        }
        finally
        {
            _updatingRows = false;
            ResumeConflictListUpdates();
        }

        UpdateHeaderChecks();
    }

    private void ApplyConflictFilter()
    {
        bool skipSameDateAndSize = _chkSkipSameDateSize.Checked;
        int visibleCount = 0;
        bool resetBodyScrollToTop = false;

        SuspendConflictListUpdates();

        try
        {
            ConflictRowPanel? previousVisibleRow = null;

            foreach (ConflictRowPanel row in _rows)
            {
                bool include = !skipSameDateAndSize || !row.IsSameDateAndSize;

                row.IncludedInList = include;
                row.Visible = include;
                row.DrawBottomSeparator = false;

                if (!include)
                {
                    row.ClearSelection();
                    continue;
                }

                if (previousVisibleRow != null)
                    previousVisibleRow.DrawBottomSeparator = true;

                previousVisibleRow = row;
                visibleCount++;
            }

            _lblEmptyMessage.Visible = skipSameDateAndSize && visibleCount == 0;
            resetBodyScrollToTop = _lblEmptyMessage.Visible;

            if (resetBodyScrollToTop)
                ResetBodyScrollToTop();

            _lblEmptyMessage.BringToFront();

            UpdateHeaderChecks();
            LayoutBodyContent();
        }
        finally
        {
            ResumeConflictListUpdates();
        }

        if (resetBodyScrollToTop)
            ResetBodyScrollToTop();
    }

    private void ResetBodyScrollToTop()
    {
        if (!_bodyPanel.IsDisposed)
            _bodyPanel.AutoScrollPosition = Point.Empty;
    }

    private void SuspendConflictListUpdates()
    {
        SuspendLayout();
        _bodyPanel.SuspendLayout();
        _listPanel.SuspendLayout();

        SetRedraw(_bodyPanel, enabled: false);
        SetRedraw(_listPanel, enabled: false);
    }

    private void ResumeConflictListUpdates()
    {
        _listPanel.ResumeLayout(performLayout: true);
        _bodyPanel.ResumeLayout(performLayout: true);
        ResumeLayout(performLayout: true);

        SetRedraw(_listPanel, enabled: true);
        SetRedraw(_bodyPanel, enabled: true);

        _listPanel.Invalidate(invalidateChildren: true);
        _bodyPanel.Invalidate(invalidateChildren: true);
    }

    private static void SetRedraw(Control control, bool enabled)
    {
        if (!control.IsHandleCreated)
            return;

        User32.SendMessage(
            control.Handle,
            WmSetRedraw,
            enabled ? new IntPtr(1) : IntPtr.Zero,
            IntPtr.Zero);
    }

    private void UpdateHeaderChecks()
    {
        _updatingHeaderChecks = true;

        try
        {
            bool anyRows = false;
            bool allSource = true;
            bool allDestination = true;

            foreach (ConflictRowPanel row in _rows)
            {
                if (!row.IncludedInList)
                    continue;

                anyRows = true;
                allSource &= row.SourceChecked;
                allDestination &= row.DestinationChecked;
            }

            if (!anyRows)
            {
                allSource = false;
                allDestination = false;
            }

            _chkSourceAll.Checked = allSource;
            _chkDestinationAll.Checked = allDestination;

            _chkSourceAll.Enabled = anyRows;
            _chkDestinationAll.Enabled = anyRows;

            _lblSourceAllPrefix.Enabled = anyRows;
            _lblDestinationAllPrefix.Enabled = anyRows;

            _lnkSourceFolder.Enabled = anyRows;
            _lnkDestinationFolder.Enabled = anyRows;
        }
        finally
        {
            _updatingHeaderChecks = false;
        }
    }

    private void LayoutFooterButtons()
    {
        int top = _footerSeparator.Bottom + _mPx.FooterSeparatorGap;

        _btnCancel.Left = _footerPanel.ClientSize.Width - _mPx.ContentMargin - _btnCancel.Width;
        _btnContinue.Left = _btnCancel.Left - _mPx.ButtonGap - _btnContinue.Width;

        _btnContinue.Top = top;
        _btnCancel.Top = top;

        _chkSkipSameDateSize.Left = _mPx.ContentMargin;
        _chkSkipSameDateSize.Top = top + _mPx.FooterCheckBoxTopNudge;
        _chkSkipSameDateSize.Width = Math.Max(
            ScaleDip(120),
            _btnContinue.Left - _mPx.ContentMargin - _mPx.ButtonGap);
    }

    private void LayoutBodyContent()
    {
        int fullWidth = Math.Max(ScaleDip(300), _bodyPanel.ClientSize.Width);
        int contentWidth = Math.Max(ScaleDip(300), fullWidth - (_mPx.ContentMargin * 2));

        _listPanel.Left = 0;
        _listPanel.Width = fullWidth;

        _columnHeaderPanel.Left = 0;
        _columnHeaderPanel.Width = _headerPanel.ClientSize.Width;
        _columnHeaderSeparator.Width = _columnHeaderPanel.Width;

        int columnWidth = Math.Max(ScaleDip(120), (contentWidth - _mPx.ColumnGap) / 2);

        LayoutColumnHeader(
            _chkSourceAll,
            _lblSourceAllPrefix,
            _lnkSourceFolder,
            _mPx.ContentMargin,
            columnWidth,
            _mPx);

        LayoutColumnHeader(
            _chkDestinationAll,
            _lblDestinationAllPrefix,
            _lnkDestinationFolder,
            _mPx.ContentMargin + columnWidth + _mPx.ColumnGap,
            columnWidth,
            _mPx);

        _lblEmptyMessage.Width = fullWidth;
        _lblEmptyMessage.Left = 0;
        _lblEmptyMessage.Top = _mPx.EmptyMessageTop;

        foreach (ConflictRowPanel row in _rows)
            row.ApplyWidth(fullWidth, _mPx.ColumnGap);
    }

    private static void LayoutColumnHeader(
        CheckBox checkBox,
        Label prefixLabel,
        LinkLabel linkLabel,
        int left,
        int width,
        CompareLayoutMetricsPx metrics)
    {
        int textTop = metrics.HeaderCheckTop;

        checkBox.Left = left;
        checkBox.Top = textTop;
        checkBox.Width = metrics.HeaderCheckBoxWidth;
        checkBox.Height = metrics.HeaderCheckBoxHeight;

        prefixLabel.Left = checkBox.Right + metrics.HeaderTextGap;
        prefixLabel.Top = textTop;
        prefixLabel.Height = metrics.HeaderCheckBoxHeight;

        linkLabel.Left = prefixLabel.Right + metrics.HeaderTextGap;
        linkLabel.Top = textTop + metrics.HeaderLinkTopOffset;
        linkLabel.Height = metrics.HeaderCheckBoxHeight;
        linkLabel.Width = Math.Max(20, left + width - linkLabel.Left);
    }

    private static string BuildTitle(int conflictCount)
    {
        return conflictCount == 1
            ? "1 File Conflict"
            : conflictCount.ToString("N0") + " File Conflicts";
    }

    private static string GetFolderDisplayName(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return "destination";

        string trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
            return folderPath;

        string name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private static Label CreateHeaderPrefixLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            TextAlign = ContentAlignment.TopLeft,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            UseMnemonic = false
        };
    }

    private LinkLabel CreatePathLinkLabel()
    {
        LinkLabel label = new()
        {
            AutoEllipsis = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            TabStop = false
        };

        label.LinkClicked += PathLink_LinkClicked;
        return label;
    }

    private void SetPathLink(LinkLabel label, string text, string toolTipText)
    {
        if (string.IsNullOrWhiteSpace(text))
            text = "Unknown";

        string path = string.IsNullOrWhiteSpace(toolTipText) ? text : toolTipText;

        label.Text = text;
        label.Tag = path;
        label.LinkArea = new LinkArea(0, text.Length);
        _pathToolTip.SetToolTip(label, path);
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

    private static string BuildSkipSameDateSizeText(int count)
    {
        string fileWord = count == 1 ? "file" : "files";
        return $"Skip {count:N0} {fileWord} with the same date and size";
    }

    private static bool IsSameDateAndSize(string sourcePath, string destinationPath)
    {
        try
        {
            if (!File.Exists(sourcePath) || !File.Exists(destinationPath))
                return false;

            FileInfo sourceInfo = new(sourcePath);
            FileInfo destinationInfo = new(destinationPath);

            return sourceInfo.Length == destinationInfo.Length &&
                   sourceInfo.LastWriteTimeUtc == destinationInfo.LastWriteTimeUtc;
        }
        catch
        {
            return false;
        }
    }

    public void FocusContinueButton()
    {
        _btnContinue.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (ConflictRowPanel row in _rows)
                row.DisposeImages();

            _pathToolTip.Dispose();
            _bodyFont?.Dispose();
            _headerFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class ConflictRowPanel : Panel
    {
        public bool DrawBottomSeparator { get; set; } = true;
        public bool IsSameDateAndSize { get; }
        public bool IncludedInList { get; set; } = true;

        private readonly ExplorerTransferConflictItem _item;
        private readonly ChoicePanel _sourceChoice;
        private readonly ChoicePanel _destinationChoice;
        private readonly Label _lblFileName;
        private CompareLayoutMetricsPx _mPx = CompareLayoutMetricsPx.Empty;
        private bool _updatingChecks;

        public event EventHandler? SelectionChanged;

        public ConflictRowPanel(
            IExplorerFileAssociationService fileAssociations,
            ExplorerTransferConflictItem item,
            bool isSameDateAndSize)
        {
            IsSameDateAndSize = isSameDateAndSize;
            IncludedInList = true;
            _item = item;
            Margin = Padding.Empty;

            _lblFileName = new Label
            {
                AutoEllipsis = true,
                Text = item.FileName,
                UseMnemonic = false
            };

            _sourceChoice = new ChoicePanel(fileAssociations, item.SourcePath);
            _sourceChoice.CheckBox.CheckedChanged += SourceCheckBox_CheckedChanged;

            _destinationChoice = new ChoicePanel(fileAssociations, item.DestinationPath);
            _destinationChoice.CheckBox.CheckedChanged += DestinationCheckBox_CheckedChanged;

            Controls.Add(_lblFileName);
            Controls.Add(_sourceChoice);
            Controls.Add(_destinationChoice);
        }

        public string SourcePath => _item.SourcePath;

        public bool SourceChecked
        {
            get => _sourceChoice.CheckBox.Checked;
            set
            {
                _updatingChecks = true;

                try
                {
                    _sourceChoice.CheckBox.Checked = value;
                }
                finally
                {
                    _updatingChecks = false;
                }

                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool DestinationChecked
        {
            get => _destinationChoice.CheckBox.Checked;
            set
            {
                _updatingChecks = true;

                try
                {
                    _destinationChoice.CheckBox.Checked = value;
                }
                finally
                {
                    _updatingChecks = false;
                }

                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ExplorerTransferConflictAction Action
        {
            get
            {
                if (SourceChecked && DestinationChecked)
                    return ExplorerTransferConflictAction.CopyWithNewName;

                if (SourceChecked)
                    return ExplorerTransferConflictAction.Overwrite;

                return ExplorerTransferConflictAction.Skip;
            }
        }

        public void SetColumnChecked(bool sourceColumn, bool isChecked)
        {
            if (sourceColumn)
                SourceChecked = isChecked;
            else
                DestinationChecked = isChecked;
        }

        public void ApplyDpi(CompareLayoutMetricsPx metrics)
        {
            _mPx = metrics;
            Height = _mPx.BodyRowHeight;

            _lblFileName.Top = _mPx.RowFileNameTop;
            _lblFileName.Height = _mPx.BodyLineHeight;

            _sourceChoice.ApplyDpi(_mPx);
            _destinationChoice.ApplyDpi(_mPx);
        }

        public void ApplyWidth(int width, int columnGap)
        {
            Width = width;

            int contentLeft = _mPx.ContentMargin;
            int contentWidth = Math.Max(_mPx.MinimumContentWidth, width - (_mPx.ContentMargin * 2));

            _lblFileName.Left = contentLeft;
            _lblFileName.Width = contentWidth;

            int columnWidth = Math.Max(_mPx.MinimumColumnWidth, (contentWidth - columnGap) / 2);

            _sourceChoice.Left = contentLeft;
            _sourceChoice.Top = _mPx.RowChoiceTop;
            _sourceChoice.Width = columnWidth;
            _sourceChoice.Height = _mPx.RowIconSize;
            _sourceChoice.LayoutContent();

            _destinationChoice.Left = contentLeft + columnWidth + columnGap;
            _destinationChoice.Top = _mPx.RowChoiceTop;
            _destinationChoice.Width = columnWidth;
            _destinationChoice.Height = _mPx.RowIconSize;
            _destinationChoice.LayoutContent();
        }

        public void ClearSelection()
        {
            _updatingChecks = true;

            try
            {
                _sourceChoice.CheckBox.Checked = false;
                _destinationChoice.CheckBox.Checked = false;
            }
            finally
            {
                _updatingChecks = false;
            }
        }

        public void DisposeImages()
        {
            _sourceChoice.DisposeImage();
            _destinationChoice.DisposeImage();
        }

        private void SourceCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (_updatingChecks)
                return;

            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DestinationCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (_updatingChecks)
                return;

            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (DrawBottomSeparator)
                e.Graphics.DrawLine(SystemPens.ControlDark, 0, Height - 1, Width, Height - 1);
        }

        private sealed class ChoicePanel : Panel
        {
            private readonly IExplorerFileAssociationService _fileAssociations;
            private readonly string _path;
            private readonly PictureBox _picIcon;
            private readonly Label _lblModified;
            private readonly Label _lblSize;
            private CompareLayoutMetricsPx _mPx = CompareLayoutMetricsPx.Empty;
            private int _iconSize;

            public ChoicePanel(IExplorerFileAssociationService fileAssociations, string path)
            {
                _fileAssociations = fileAssociations;
                _path = path;

                CheckBox = new CheckBox
                {
                    Left = 0,
                    Top = 0
                };

                _picIcon = new PictureBox
                {
                    Left = 0,
                    Top = 0,
                    SizeMode = PictureBoxSizeMode.CenterImage
                };

                _lblModified = new Label
                {
                    AutoEllipsis = true,
                    Text = FileOperationText.GetDateModifiedText(path),
                    UseMnemonic = false
                };

                _lblSize = new Label
                {
                    AutoEllipsis = true,
                    Text = FileOperationText.GetSizeText(path),
                    UseMnemonic = false
                };

                Controls.Add(CheckBox);
                Controls.Add(_picIcon);
                Controls.Add(_lblModified);
                Controls.Add(_lblSize);
            }

            public CheckBox CheckBox { get; }

            public void ApplyDpi(CompareLayoutMetricsPx metrics)
            {
                _mPx = metrics;

                CheckBox.Width = _mPx.RowCheckBoxSize;
                CheckBox.Height = _mPx.RowCheckBoxSize;

                _picIcon.Width = _mPx.RowIconSize;
                _picIcon.Height = _mPx.RowIconSize;

                _lblModified.Top = _mPx.ChoiceModifiedTop;
                _lblModified.Height = _mPx.ChoiceTextLineHeight;

                _lblSize.Top = _mPx.ChoiceSizeTop;
                _lblSize.Height = _mPx.ChoiceTextLineHeight;

                RefreshImage(_mPx.RowIconSize);
            }

            public void LayoutContent()
            {
                CheckBox.Left = 0;
                CheckBox.Top = 0;

                _picIcon.Left = CheckBox.Right + _mPx.CellInnerGap;
                _picIcon.Top = 0;

                int textLeft = _picIcon.Right + _mPx.CellInnerGap;
                int textWidth = Math.Max(20, Width - textLeft);

                _lblModified.Left = textLeft;
                _lblModified.Width = textWidth;

                _lblSize.Left = textLeft;
                _lblSize.Width = textWidth;
            }

            public void DisposeImage()
            {
                Image? oldImage = _picIcon.Image;
                _picIcon.Image = null;
                oldImage?.Dispose();
            }

            private void RefreshImage(int iconSize)
            {
                if (_iconSize == iconSize && _picIcon.Image != null)
                    return;

                DisposeImage();
                _iconSize = iconSize;
                _picIcon.Image = CreateFileImage(_fileAssociations, _path, _iconSize);
            }

            private static Image? CreateFileImage(
                IExplorerFileAssociationService fileAssociations,
                string path,
                int size)
            {
                try
                {
                    return UiExplorerIconCache.CreateUncachedFileSystemItemImage(
                        fileAssociations,
                        path,
                        isDirectory: false,
                        size: size);
                }
                catch
                {
                    return IconUtil.FromGenericFile(size);
                }
            }
        }
    }

    private sealed class CompareLayoutMetricsPx
    {
        public static CompareLayoutMetricsPx Empty { get; } = FromDip(
            DefaultCompareViewWidthDip,
            static dip => dip,
            ShellDialogChrome.DialogFont,
            ShellDialogChrome.DialogFont);

        public int ViewWidth { get; init; }
        public int ClientHeight { get; init; }
        public int HeaderHeight { get; init; }
        public int BodyHeight { get; init; }
        public int FooterHeight { get; init; }
        public int ContentMargin { get; init; }
        public int HeaderBottomPadding { get; init; }
        public int HeaderQuestionTop { get; init; }
        public int HeaderDetailTop { get; init; }
        public int ColumnHeaderTop { get; init; }
        public int ColumnHeaderHeight { get; init; }
        public int HeaderCheckTop { get; init; }
        public int HeaderCheckBoxWidth { get; init; }
        public int HeaderCheckBoxHeight { get; init; }
        public int HeaderTextGap { get; init; }
        public int HeaderLinkTopOffset { get; init; }
        public int HeaderLineHeight { get; init; }
        public int BodyLineHeight { get; init; }
        public int BodyRowHeight { get; init; }
        public int FooterSeparatorGap { get; init; }
        public int FooterCheckBoxTopNudge { get; init; }
        public int ButtonWidth { get; init; }
        public int ButtonHeight { get; init; }
        public int ButtonGap { get; init; }
        public int ColumnGap { get; init; }
        public int EmptyMessageTop { get; init; }
        public int MinimumContentWidth { get; init; }
        public int MinimumColumnWidth { get; init; }
        public int RowFileNameTop { get; init; }
        public int RowChoiceTop { get; init; }
        public int RowIconSize { get; init; }
        public int RowCheckBoxSize { get; init; }
        public int CellInnerGap { get; init; }
        public int ChoiceModifiedTop { get; init; }
        public int ChoiceSizeTop { get; init; }
        public int ChoiceTextLineHeight { get; init; }

        public static CompareLayoutMetricsPx FromDip(
            int viewWidth,
            Func<int, int> scale,
            Font bodyFont,
            Font headerFont)
        {
            int bodyLineHeight = Math.Max(
                scale(BodyLineHeightDip),
                bodyFont.Height + scale(FontHeightPaddingDip));

            int headerLineHeight = Math.Max(
                scale(HeaderLineHeightDip),
                headerFont.Height + scale(FontHeightPaddingDip));

            int buttonHeight = Math.Max(
                scale(ButtonHeightDip),
                bodyFont.Height + scale(10));

            int headerHeight = scale(CompareHeaderHeightDip);
            int footerHeight = Math.Max(scale(CompareFooterHeightDip), buttonHeight + scale(21));
            int bodyRowHeight = scale(BodyRowHeightDip);
            int bodyHeight = MaxVisibleBodyRows * bodyRowHeight;

            return new CompareLayoutMetricsPx
            {
                ViewWidth = viewWidth,
                ClientHeight = headerHeight + bodyHeight + footerHeight,
                HeaderHeight = headerHeight,
                BodyHeight = bodyHeight,
                FooterHeight = footerHeight,
                ContentMargin = scale(ContentMarginDip),
                HeaderBottomPadding = scale(8),
                HeaderQuestionTop = scale(HeaderQuestionTopDip),
                HeaderDetailTop = scale(HeaderDetailTopDip),
                ColumnHeaderTop = scale(ColumnHeaderTopDip),
                ColumnHeaderHeight = scale(ColumnHeaderHeightDip),
                HeaderCheckTop = scale(HeaderCheckTopDip),
                HeaderCheckBoxWidth = scale(HeaderCheckBoxWidthDip),
                HeaderCheckBoxHeight = Math.Max(scale(18), bodyLineHeight),
                HeaderTextGap = scale(HeaderTextGapDip),
                HeaderLinkTopOffset = scale(HeaderLinkTopOffsetDip),
                HeaderLineHeight = headerLineHeight,
                BodyLineHeight = bodyLineHeight,
                BodyRowHeight = bodyRowHeight,
                FooterSeparatorGap = scale(FooterSeparatorGapDip),
                FooterCheckBoxTopNudge = scale(3),
                ButtonWidth = scale(ButtonWidthDip),
                ButtonHeight = buttonHeight,
                ButtonGap = scale(ButtonGapDip),
                ColumnGap = scale(ColumnGapDip),
                EmptyMessageTop = scale(EmptyMessageTopDip),
                MinimumContentWidth = scale(300),
                MinimumColumnWidth = scale(120),
                RowFileNameTop = scale(4),
                RowChoiceTop = scale(27),
                RowIconSize = scale(48),
                RowCheckBoxSize = scale(18),
                CellInnerGap = scale(5),
                ChoiceModifiedTop = scale(2),
                ChoiceSizeTop = scale(19),
                ChoiceTextLineHeight = Math.Max(scale(16), bodyFont.Height + scale(1))
            };
        }
    }
}
