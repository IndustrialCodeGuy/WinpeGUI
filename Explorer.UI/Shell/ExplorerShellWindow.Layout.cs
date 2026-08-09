using Explorer.UI.Layout;
using Shared.Shell.Theming;
using Shell.Core.Models;
using Shared.Shell.Interop;
using System.Runtime.InteropServices;

namespace Explorer.UI.Shell;

public partial class ExplorerShellWindow
{
    private void InitializeExplorerChrome()
    {
        StartPosition = FormStartPosition.CenterScreen;

        RecalcMetrics();
        _appliedDpi = DeviceDpi;

        ApplyExplorerMinimumSize();
        ClientSize = new Size(_mPx.InitialWidth, _mPx.InitialHeight);
        TrackNormalClientSize();

        RebuildFonts();
        ConfigureToolbarButtons();
        ConfigureShellControls();
        ConfigurePickerControls();
        ApplyLayoutMetrics();
    }

    private void ExplorerShellWindow_Layout(object? sender, EventArgs e)
    {
        QueueApplyLayoutMetrics();
    }

    private void ApplyInitialWindowPlacement(ExplorerWindowPlacement? placement)
    {
        if (_mode != ExplorerWindowMode.Browse || placement is null)
            return;

        _maxNavPaneWidthDip = Math.Max(GetMinimumNavPaneWidthDip(), placement.NavPaneWidthDip);
        _lastSplitAvailablePaneWidth = 0;
        _lastNavPaneWidth = 0;

        Rectangle bounds = FitBoundsToDpiAvailableArea(placement.Bounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        ApplyLayoutMetrics();
        TrackNormalClientSize();

        if (placement.IsMaximized)
            WindowState = FormWindowState.Maximized;
        else            
            WindowState = FormWindowState.Normal;
        
    }

    private void QueueApplyLayoutMetrics()
    {
        if (IsDisposed || _applyingLayoutMetrics)
            return;

        if (!IsHandleCreated)
        {
            ApplyLayoutMetrics();
            return;
        }

        if (_layoutMetricsApplyQueued)
            return;

        _layoutMetricsApplyQueued = true;

        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                _layoutMetricsApplyQueued = false;

                if (!IsDisposed)
                    ApplyLayoutMetrics();
            }));
        }
        catch (InvalidOperationException)
        {
            _layoutMetricsApplyQueued = false;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ReapplyDpiMetrics(refreshContent: false);
        TrackNormalClientSize();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        TrackNormalClientSize();
    }

    private void RecalcMetrics()
    {
        _mPx = ExplorerLayoutMetricsPx.FromDip(_mDip, ScaleDip, ScaleFontPointToPx);
    }

    private void RebuildFonts()
    {
        // Build replacement fonts before disposing the old ones. RebuildFonts can run
        // after native handles exist during DPI changes, so avoid briefly leaving
        // controls with references to disposed font objects during a font rebuild.
        Font toolbarGlyphFont = CreateToolbarGlyphFont();
        Font addressFont = CreateUiPixelFont("Segoe UI", _mPx.AddressFontSize, FontStyle.Regular);
        Font addressSeparatorFont = CreateUiPixelFont("Segoe UI", _mPx.AddressSeparatorFontSize, FontStyle.Regular);
        Font chromeFont = CreateUiPixelFont("Segoe UI", _mPx.ChromeFontSize, FontStyle.Regular);

        Font? oldToolbarGlyphFont = _toolbarGlyphFont;
        Font? oldAddressFont = _addressFont;
        Font? oldAddressSeparatorFont = _addressSeparatorFont;
        Font? oldChromeFont = _chromeFont;

        _toolbarGlyphFont = toolbarGlyphFont;
        _addressFont = addressFont;
        _addressSeparatorFont = addressSeparatorFont;
        _chromeFont = chromeFont;

        ApplyToolbarGlyphFonts();

        // Keep the managed TextBox Font neutral. The native edit HWND receives the
        // shell-owned address font through WM_SETFONT below, avoiding WinForms
        // Control.Font inheritance/scaling while preserving normal edit behavior.
        _txtPath.Font = Font;
        ApplyAddressTextBoxNativeFont();

        // Breadcrumb items are owner-painted and receive _addressFont through
        // AddressBreadcrumbItem.TextFont. Keep the host/control-tree Font neutral
        // so WinForms font inheritance/scaling cannot affect the draw font.
        _addressLinkPanel.Font = Font;

        // Standard explorer text should not use the form/default Font. Keep it on
        // the same shell-owned pixel-font scaling path used by the taskbar.
        ApplyChromeFonts();

        oldToolbarGlyphFont?.Dispose();
        oldAddressFont?.Dispose();
        oldAddressSeparatorFont?.Dispose();
        oldChromeFont?.Dispose();
    }

    private void ApplyToolbarGlyphFont(Button button)
    {
        if (button.IsDisposed || !button.IsHandleCreated || _toolbarGlyphFont is null)
            return;

        button.Font = _toolbarGlyphFont;
    }

    private void ApplyToolbarGlyphFonts()
    {
        ApplyToolbarGlyphFont(_btnBack);
        ApplyToolbarGlyphFont(_btnForward);
        ApplyToolbarGlyphFont(_btnUp);
        ApplyToolbarGlyphFont(_btnRefresh);
    }

    private void ApplyChromeFont(Control control)
    {
        if (control.IsDisposed || !control.IsHandleCreated || _chromeFont is null)
            return;

        control.Font = _chromeFont;
    }

    private void ApplyChromeFonts()
    {
        ApplyChromeFont(_lblStatus);
        ApplyChromeFont(_lblSelection);
        ApplyChromeFont(_lblFileType);
        ApplyChromeFont(_txtFileName);
        ApplyChromeFont(_cmbFileType);
        ApplyChromeFont(_btnOk);
        ApplyChromeFont(_btnCancel);
        ApplyChromeFont(_tvNav);
        ApplyChromeFont(_lvItems);
    }

    private const int WM_SETFONT = 0x0030;

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    private void ApplyAddressTextBoxNativeFont()
    {
        if (_txtPath.IsDisposed || !_txtPath.IsHandleCreated)
            return;

        Font font = _addressFont ?? _txtPath.Font;
        IntPtr oldHFont = _addressTextBoxHFont;
        IntPtr newHFont = font.ToHfont();

        // wParam: HFONT
        // lParam: TRUE to redraw
        //
        // The TextBox does not own this HFONT. Keep it alive until the next
        // address-font rebuild, handle destroy, or window close. Replace the
        // native control font before deleting the previous HFONT so we never
        // delete a font that is still selected into the edit control.
        _addressTextBoxHFont = newHFont;
        User32.SendMessage(_txtPath.Handle, WM_SETFONT, newHFont, new IntPtr(1));

        if (oldHFont != IntPtr.Zero)
            DeleteObject(oldHFont);
        _txtPath.Invalidate();
    }

    private void DisposeAddressTextBoxNativeFont()
    {
        IntPtr hFont = _addressTextBoxHFont;
        if (hFont == IntPtr.Zero)
            return;

        _addressTextBoxHFont = IntPtr.Zero;
        DeleteObject(hFont);
    }

    private void ApplyTreeViewDpiMetrics()
    {
        int indent = Math.Max(19, ScaleDip(19));
        int itemHeight = Math.Max(16, _mPx.SmallImageSize.Height + ScaleDip(4));

        bool treeUpdateStarted = BeginTreeUpdateIfDpiRedrawNotFrozen(_tvNav);
        try
        {
            // TreeView can keep stale native indent/hit-test state when DPI is
            // reduced. Nudge first if the managed property already has the
            // target value so the native control still receives a real reset.
            if (_tvNav.Indent == indent)
                _tvNav.Indent = indent + 1;

            _tvNav.Indent = indent;

            if (_tvNav.ItemHeight == itemHeight)
                _tvNav.ItemHeight = itemHeight + 1;

            _tvNav.ItemHeight = itemHeight;
        }
        finally
        {
            EndTreeUpdateIfStarted(_tvNav, treeUpdateStarted);
        }
    }

    private void ConfigureToolbarButtons()
    {
        SetToolbarGlyphButtonEnabled(_btnBack, false);
        SetToolbarGlyphButtonEnabled(_btnForward, false);
        SetToolbarGlyphButtonEnabled(_btnUp, false);

        ConfigureToolbarGlyphButton(_btnBack, "\uE0A6");
        ConfigureToolbarGlyphButton(_btnForward, "\uE0AB");
        ConfigureToolbarGlyphButton(_btnUp, "\uE110");
        ConfigureToolbarGlyphButton(_btnRefresh, "\uE149");
    }

    private void ConfigureShellControls()
    {
        InitializeImageLists();
        EnableDoubleBuffering(_tvNav);
        EnableDoubleBuffering(_lvItems);
        EnableDoubleBuffering(_addressLinkPanel);

        ApplyThemeColors();

        _splitMain.IsSplitterFixed = true;

        _tvNav.BorderStyle = BorderStyle.FixedSingle;
        _tvNav.DrawMode = TreeViewDrawMode.OwnerDrawText;

        _lvItems.BorderStyle = BorderStyle.FixedSingle;
        _lvItems.AllowColumnReorder = true;
        _lvItems.OwnerDraw = true;
        _lvItems.UseCompatibleStateImageBehavior = false;
        _lvItems.MultiSelect = _mode == ExplorerWindowMode.Browse;

        ApplyAddressBarChromeMetrics();

        _lblStatus.Text = "Ready";
    }

    private void ApplyThemeColors()
    {
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        _topPanel.BackColor = ShellTheme.WindowBack;
        _bottomPanel.BackColor = ShellTheme.WindowBack;
        _splitMain.BackColor = ShellTheme.WindowBack;
        _splitMain.Panel1.BackColor = ShellTheme.ContentBack;
        _splitMain.Panel2.BackColor = ShellTheme.ContentBack;
        _splitMain.FixedPanel = FixedPanel.Panel1;

        _tvNav.BackColor = ShellTheme.ContentBack;
        _tvNav.ForeColor = ShellTheme.TextColor;
        _lvItems.BackColor = ShellTheme.ContentBack;
        _lvItems.ForeColor = ShellTheme.TextColor;

        _lblSelection.ForeColor = ShellTheme.TextColor;
        _lblStatus.ForeColor = ShellTheme.TextColor;
        _lblFileType.ForeColor = ShellTheme.TextColor;

        _txtFileName.BackColor = ShellTheme.ContentBack;
        _txtFileName.ForeColor = ShellTheme.TextColor;

        foreach (Button button in new[] { _btnBack, _btnForward, _btnUp, _btnRefresh })
            ApplyToolbarButtonTheme(button);

        InvalidateThemedListHeader();
    }

    private static void ApplyToolbarButtonTheme(Button button)
    {
        button.BackColor = ShellTheme.ButtonDefault;
        SetToolbarGlyphButtonEnabled(button, IsToolbarGlyphButtonEnabled(button));
    }


    private void ConfigurePickerControls()
    {
        _cmbFileType.Items.Clear();

        foreach (string extension in _allowedExtensionsDisplay)
            _cmbFileType.Items.Add($"{extension.TrimStart('.').ToUpperInvariant()} Files (*{extension})");

        _cmbFileType.Items.Add("All Files (*.*)");
        _cmbFileType.SelectedIndex = 0;

        bool isOpenOrSave = _mode is ExplorerWindowMode.OpenFile or ExplorerWindowMode.SaveFile;
        bool isPicker = _mode != ExplorerWindowMode.Browse;

        _lblSelection.Visible = isOpenOrSave;
        _lblSelection.Text = "File name:";
        _txtFileName.Visible = isOpenOrSave;
        _cmbFileType.Visible = isOpenOrSave;
        _lblFileType.Visible = false;
        _btnOk.Visible = isPicker;
        _btnCancel.Visible = isPicker;
        _lblStatus.Visible = !isOpenOrSave;

        _btnOk.Text = _mode switch
        {
            ExplorerWindowMode.OpenFile => "Open",
            ExplorerWindowMode.SelectFolder => "Select Folder",
            ExplorerWindowMode.SaveFile => "Save",
            _ => "OK"
        };
    }

    private void ApplyLayoutMetrics()
    {
        if (_applyingLayoutMetrics)
            return;

        _applyingLayoutMetrics = true;

        try
        {
            SetHeightIfChanged(_topPanel, _mPx.TopBarHeight);
            SetPaddingIfChanged(_topPanel, _mPx.TopBarPadding);
            SetHeightIfChanged(_bottomPanel, _mode == ExplorerWindowMode.Browse
                ? _mPx.BottomBarBrowseHeight
                : _mPx.BottomBarHeight);

            SetPaddingIfChanged(_bottomPanel, _mode == ExplorerWindowMode.Browse
                ? _mPx.BottomBarBrowsePadding
                : _mPx.BottomBarPadding);

            ApplySplitPaneMetrics();

            int x = _mPx.TopBarPadding.Left;
            int y = _mPx.TopBarPadding.Top;

            ApplyToolbarButtonBounds(_btnBack, x, y);
            x += _btnBack.Width + _mPx.ToolbarButtonGap;

            ApplyToolbarButtonBounds(_btnForward, x, y);
            x += _btnForward.Width + _mPx.ToolbarButtonGap;

            ApplyToolbarButtonBounds(_btnUp, x, y);
            x += _btnUp.Width + _mPx.ToolbarButtonGap;

            ApplyToolbarButtonBounds(_btnRefresh, x, y);

            int pathLeft = _mPx.AddressHostLeft;
            int pathRight = _topPanel.ClientSize.Width - _mPx.TopBarPadding.Right - _mPx.AddressHostRightGap;
            int pathWidth = Math.Max(100, pathRight - pathLeft);

            SetBoundsIfChanged(_pathHost, pathLeft, _mPx.AddressHostTop, pathWidth, _mPx.AddressHostHeight);

            ApplyAddressBarChromeMetrics();
            PerformBottomBarLayout();
        }
        finally
        {
            _applyingLayoutMetrics = false;
        }
    }

    private void SplitMain_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (!GetSplitMainSplitterBounds().Contains(e.Location))
            return;

        _draggingSplitMainSplitter = true;
        _splitMain.Capture = true;

        _splitMainSplitterDragOffset = _splitMain.Orientation == Orientation.Vertical
            ? e.X - _splitMain.SplitterDistance
            : e.Y - _splitMain.SplitterDistance;

        SetSplitMainSplitterCursor();
    }

    private void SplitMain_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggingSplitMainSplitter)
        {
            int requestedNavWidth = _splitMain.Orientation == Orientation.Vertical
                ? e.X - _splitMainSplitterDragOffset
                : e.Y - _splitMainSplitterDragOffset;

            ApplyManualSplitPaneWidth(requestedNavWidth);
            return;
        }

        Cursor targetCursor = GetSplitMainSplitterBounds().Contains(e.Location)
            ? GetSplitMainSplitterCursor()
            : Cursors.Default;

        if (_splitMain.Cursor != targetCursor)
            _splitMain.Cursor = targetCursor;
    }

    private void SplitMain_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_draggingSplitMainSplitter)
            return;

        int requestedNavWidth = _splitMain.Orientation == Orientation.Vertical
            ? e.X - _splitMainSplitterDragOffset
            : e.Y - _splitMainSplitterDragOffset;

        ApplyManualSplitPaneWidth(requestedNavWidth);

        _draggingSplitMainSplitter = false;
        _splitMain.Capture = false;

        CommitManualSplitPaneWidth();

        Cursor targetCursor = GetSplitMainSplitterBounds().Contains(e.Location)
            ? GetSplitMainSplitterCursor()
            : Cursors.Default;

        if (_splitMain.Cursor != targetCursor)
            _splitMain.Cursor = targetCursor;
    }

    private void SplitMain_MouseLeave(object? sender, EventArgs e)
    {
        if (_draggingSplitMainSplitter)
            return;

        if (_splitMain.Cursor != Cursors.Default)
            _splitMain.Cursor = Cursors.Default;
    }

    private void ApplyManualSplitPaneWidth(int requestedNavWidth)
    {
        int availablePaneWidth = GetAvailableSplitPaneWidth();
        if (availablePaneWidth <= 0)
            return;

        int nextNavWidth = ClampManualNavPaneWidth(requestedNavWidth, availablePaneWidth);

        if (_splitMain.SplitterDistance != nextNavWidth)
            _splitMain.SplitterDistance = nextNavWidth;

        _lastSplitAvailablePaneWidth = availablePaneWidth;
        _lastNavPaneWidth = nextNavWidth;
    }

    private void CommitManualSplitPaneWidth()
    {
        int availablePaneWidth = GetAvailableSplitPaneWidth();
        if (availablePaneWidth <= 0)
            return;

        int nextNavWidth = ClampManualNavPaneWidth(_splitMain.SplitterDistance, availablePaneWidth);

        if (_splitMain.SplitterDistance != nextNavWidth)
            _splitMain.SplitterDistance = nextNavWidth;

        // The user's final manual splitter position is the tree maximum.
        _maxNavPaneWidthDip = Math.Max(
            GetMinimumNavPaneWidthDip(),
            UnscaleDip(nextNavWidth));

        _lastSplitAvailablePaneWidth = availablePaneWidth;
        _lastNavPaneWidth = nextNavWidth;
    }

    private Rectangle GetSplitMainSplitterBounds()
    {
        return _splitMain.Orientation == Orientation.Vertical
            ? new Rectangle(
                _splitMain.SplitterDistance,
                0,
                _splitMain.SplitterWidth,
                _splitMain.ClientSize.Height)
            : new Rectangle(
                0,
                _splitMain.SplitterDistance,
                _splitMain.ClientSize.Width,
                _splitMain.SplitterWidth);
    }

    private Cursor GetSplitMainSplitterCursor()
    {
        return _splitMain.Orientation == Orientation.Vertical
            ? Cursors.VSplit
            : Cursors.HSplit;
    }

    private void SetSplitMainSplitterCursor()
    {
        Cursor cursor = GetSplitMainSplitterCursor();

        if (_splitMain.Cursor != cursor)
            _splitMain.Cursor = cursor;
    }

    private void ApplySplitPaneMetrics()
    {
        if (_draggingSplitMainSplitter)
            return;

        EnsureMaxNavPaneWidth();

        int availablePaneWidth = GetAvailableSplitPaneWidth();
        if (availablePaneWidth <= 0)
            return;

        int minNavWidth = Math.Min(GetMinimumNavPaneWidth(), availablePaneWidth);
        int maxNavWidth = Math.Max(minNavWidth, ScaleDip(_maxNavPaneWidthDip));
        int minListWidth = GetMinimumListPaneWidth();

        int hardMaxNavWidth = GetHardMaxNavPaneWidth();
        if (hardMaxNavWidth < minNavWidth)
            hardMaxNavWidth = minNavWidth;

        maxNavWidth = Math.Min(maxNavWidth, hardMaxNavWidth);

        int nextNavWidth;

        if (_lastSplitAvailablePaneWidth <= 0 || _lastNavPaneWidth <= 0)
        {
            // Initial layout: use the tree max, unless doing so would violate the
            // minimum list width.
            nextNavWidth = maxNavWidth;

            if (availablePaneWidth - nextNavWidth < minListWidth)
                nextNavWidth = availablePaneWidth - minListWidth;
        }
        else if (availablePaneWidth < _lastSplitAvailablePaneWidth)
        {
            // Window got narrower:
            // Keep the tree where it was until the list reaches its minimum.
            nextNavWidth = _lastNavPaneWidth;

            if (availablePaneWidth - nextNavWidth < minListWidth)
                nextNavWidth = availablePaneWidth - minListWidth;
        }
        else if (availablePaneWidth > _lastSplitAvailablePaneWidth)
        {
            // Window got wider:
            // Give the extra width back to the tree first, until it reaches the
            // user's last manual splitter position / tree maximum.
            int gainedWidth = availablePaneWidth - _lastSplitAvailablePaneWidth;
            nextNavWidth = _lastNavPaneWidth;

            if (nextNavWidth < maxNavWidth)
                nextNavWidth = Math.Min(maxNavWidth, nextNavWidth + gainedWidth);
        }
        else
        {
            // Non-size layout pass. Preserve the last committed splitter position.
            nextNavWidth = _lastNavPaneWidth;
        }

        nextNavWidth = ClampNavPaneWidth(nextNavWidth, availablePaneWidth, minNavWidth, maxNavWidth, minListWidth);

        if (_splitMain.SplitterDistance != nextNavWidth)
            _splitMain.SplitterDistance = nextNavWidth;

        _lastSplitAvailablePaneWidth = availablePaneWidth;
        _lastNavPaneWidth = nextNavWidth;
    }

    private int ClampManualNavPaneWidth(int navPaneWidth, int availablePaneWidth)
    {
        int minNavWidth = Math.Min(GetMinimumNavPaneWidth(), availablePaneWidth);
        int maxNavWidth = GetHardMaxNavPaneWidth();
        int minListWidth = GetMinimumListPaneWidth();

        if (availablePaneWidth - minListWidth >= minNavWidth)
            maxNavWidth = Math.Min(maxNavWidth, availablePaneWidth - minListWidth);

        return Math.Max(minNavWidth, Math.Min(navPaneWidth, maxNavWidth));
    }

    private int ClampNavPaneWidth(
        int navPaneWidth,
        int availablePaneWidth,
        int minNavWidth,
        int maxNavWidth,
        int minListWidth)
    {
        int nextNavWidth = Math.Max(minNavWidth, Math.Min(navPaneWidth, maxNavWidth));

        if (availablePaneWidth - minListWidth >= minNavWidth)
            nextNavWidth = Math.Min(nextNavWidth, availablePaneWidth - minListWidth);

        return Math.Max(minNavWidth, nextNavWidth);
    }

    private void EnsureMaxNavPaneWidth()
    {
        if (_maxNavPaneWidthDip > 0)
            return;

        _maxNavPaneWidthDip = _mDip.NavPaneWidthDip;
    }

    private void ResetSplitPaneResizeState()
    {
        _lastSplitAvailablePaneWidth = 0;
        _lastNavPaneWidth = 0;
    }

    private int GetAvailableSplitPaneWidth()
    {
        return _splitMain.ClientSize.Width - _splitMain.SplitterWidth;
    }

    private int GetHardMaxNavPaneWidth()
    {
        return _splitMain.ClientSize.Width - _splitMain.SplitterWidth - _splitMain.Panel2MinSize;
    }

    private int GetMinimumNavPaneWidth()
    {
        return ScaleDip(GetMinimumNavPaneWidthDip());
    }

    private static int GetMinimumNavPaneWidthDip()
    {
        return 125;
    }

    private int GetMinimumListPaneWidth()
    {
        return ScaleDip(125);
    }

    private int UnscaleDip(int px)
    {
        return (int)Math.Round(px * (96f / DeviceDpi));
    }

    private void ApplyAddressBarChromeMetrics()
    {
        _pathHost.BackColor = ShellTheme.ContentBack;
        _pathHost.BorderStyle = BorderStyle.FixedSingle;

        // Do not use _pathHost.Padding for vertical placement. Padding shrinks
        // the dock-fill child area, which was forcing the breadcrumb hover
        // highlight to stay text-row height.
        _pathHost.Padding = Padding.Empty;

        int clientWidth = Math.Max(0, _pathHost.ClientSize.Width);
        int clientHeight = Math.Max(0, _pathHost.ClientSize.Height);

        int contentLeft = Math.Min(_mPx.AddressInnerLeft, clientWidth);
        int contentRight = Math.Min(_mPx.AddressInnerRight, Math.Max(0, clientWidth - contentLeft));
        int contentWidth = Math.Max(0, clientWidth - contentLeft - contentRight);

        AddressBarBreadcrumbMetricsPx metrics = GetAddressBreadcrumbMetricsPx();

        int highlightTop = Math.Min(metrics.HighlightPadTop, clientHeight);
        int highlightBottom = Math.Min(metrics.HighlightPadBottom, Math.Max(0, clientHeight - highlightTop));
        int highlightHeight = Math.Max(1, clientHeight - highlightTop - highlightBottom);

        SetBoundsIfChanged(_addressLinkPanel, contentLeft, highlightTop, contentWidth, highlightHeight);

        int textHeight = Math.Min(_mPx.AddressTextHeight, clientHeight);
        int textTop = Math.Max(0, (clientHeight - textHeight) / 2);

        SetBoundsIfChanged(_txtPath, contentLeft, textTop, contentWidth, textHeight);

        _txtPath.BackColor = ShellTheme.ContentBack;
        _txtPath.ForeColor = ShellTheme.TextColor;
        _txtPath.Visible = _isAddressTextMode;

        _addressLinkPanel.Visible = !_isAddressTextMode;
        SetAddressLinkModeColors();

        if (!_isAddressTextMode)
            RenderAddressLinks();
    }

    private void PerformBottomBarLayout()
    {
        if (_mode == ExplorerWindowMode.Browse)
        {
            SetBoundsIfChanged(
                _lblStatus,
                _mPx.StatusLabelLeft,
                _mPx.StatusLabelBrowseTop,
                _mPx.StatusLabelWidth,
                _mPx.StatusLabelHeight);
            return;
        }

        int buttonRowWidth = _mPx.DialogButtonWidth + _mPx.DialogButtonGap + _mPx.DialogButtonWidth;
        int buttonTop = _mPx.FileNameTop + _mPx.FileNameHeight + ScaleDip(10);

        SetBoundsIfChanged(
            _btnCancel,
            _bottomPanel.ClientSize.Width - _mPx.DialogButtonWidth - _mPx.DialogButtonRight,
            buttonTop,
            _mPx.DialogButtonWidth,
            _mPx.DialogButtonHeight);

        SetBoundsIfChanged(
            _btnOk,
            _btnCancel.Left - _mPx.DialogButtonGap - _mPx.DialogButtonWidth,
            buttonTop,
            _mPx.DialogButtonWidth,
            _mPx.DialogButtonHeight);

        if (_mode is ExplorerWindowMode.OpenFile or ExplorerWindowMode.SaveFile)
        {
            int labelLeft = _mPx.StatusLabelLeft;
            int labelWidth = ScaleDip(80);
            int fileNameLeft = labelLeft + labelWidth + ScaleDip(6);
            int comboLeft = _btnOk.Left;

            SetBoundsIfChanged(_lblSelection, labelLeft, _mPx.FileNameTop + ScaleDip(3), labelWidth, _mPx.StatusLabelHeight);
            SetBoundsIfChanged(_cmbFileType, comboLeft, _mPx.FileNameTop, buttonRowWidth, _mPx.FileNameHeight);
            SetBoundsIfChanged(
                _txtFileName,
                fileNameLeft,
                _mPx.FileNameTop,
                Math.Max(100, comboLeft - fileNameLeft - ScaleDip(8)),
                _mPx.FileNameHeight);
            return;
        }

        SetBoundsIfChanged(
            _lblStatus,
            _mPx.StatusLabelLeft,
            _btnOk.Top + ((_btnOk.Height - _mPx.StatusLabelHeight) / 2),
            Math.Max(100, _btnOk.Left - _mPx.StatusLabelLeft - ScaleDip(10)),
            _mPx.StatusLabelHeight);
    }

    private void ApplyToolbarButtonBounds(Button button, int left, int top)
    {
        SetBoundsIfChanged(button, left, top, _mPx.ToolbarButtonSize, _mPx.ToolbarButtonSize);
    }

    private void ConfigureToolbarGlyphButton(Button button, string glyph)
    {
        button.Image = null;
        button.Text = glyph;
        button.Tag = null;
        button.Font = _toolbarGlyphFont;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.TextImageRelation = TextImageRelation.Overlay;

        button.UseCompatibleTextRendering = true;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;

        button.Enabled = true;
        button.BackColor = ShellTheme.ButtonDefault;
        button.TabStop = false;
        button.Padding = new Padding(0, _mPx.ToolbarGlyphTopPaddingPx, 0, 0);
        SetToolbarGlyphButtonEnabled(button, IsToolbarGlyphButtonEnabled(button));
    }

    private const string ToolbarGlyphButtonInactiveState = "Inactive";

    private static bool IsToolbarGlyphButtonEnabled(Button button)
    {
        return !string.Equals(
            button.AccessibleDescription,
            ToolbarGlyphButtonInactiveState,
            StringComparison.Ordinal);
    }

    private static void SetToolbarGlyphButtonEnabled(Button button, bool enabled)
    {
        bool wasEnabled = IsToolbarGlyphButtonEnabled(button);
        string description = enabled ? string.Empty : ToolbarGlyphButtonInactiveState;
        Color foreColor = enabled ? ShellTheme.TextColor : ShellTheme.MutedText;
        Color hoverColor = enabled ? ShellTheme.ButtonHovered : ShellTheme.ButtonDefault;
        Color downColor = enabled ? ShellTheme.ButtonPressed : ShellTheme.ButtonDefault;

        if (wasEnabled == enabled &&
            button.Enabled &&
            string.Equals(button.AccessibleDescription, description, StringComparison.Ordinal) &&
            button.ForeColor == foreColor &&
            button.FlatAppearance.MouseOverBackColor == hoverColor &&
            button.FlatAppearance.MouseDownBackColor == downColor)
        {
            return;
        }

        button.AccessibleDescription = description;

        // Keep the WinForms button itself enabled so ForeColor is honored by the
        // normal text/glyph renderer. A truly disabled Button uses native
        // disabled-text rendering, which can ignore ForeColor and offset the glyph.
        button.Enabled = true;
        button.ForeColor = foreColor;
        button.FlatAppearance.MouseOverBackColor = hoverColor;
        button.FlatAppearance.MouseDownBackColor = downColor;
        button.Invalidate();
    }

    private static void SetHeightIfChanged(Control control, int height)
    {
        if (control.Height != height)
            control.Height = height;
    }

    private static void SetPaddingIfChanged(Control control, Padding padding)
    {
        if (control.Padding != padding)
            control.Padding = padding;
    }

    private static void SetBoundsIfChanged(Control control, int x, int y, int width, int height)
    {
        Rectangle bounds = new(x, y, width, height);

        if (control.Bounds != bounds)
            control.Bounds = bounds;
    }

    private int ScaleDip(int dip)
    {
        return (int)Math.Round(dip * (DeviceDpi / 96f));
    }

    // Same model as the taskbar text path: store tuned 100% defaults as
    // point sizes, resolve them to current-DPI pixels, then create the live
    // Font with GraphicsUnit.Pixel so GDI/WinForms does not scale it again.
    private float ScaleFontPointToPx(float pointSize)
    {
        return pointSize * (DeviceDpi / 72f);
    }

    private Font CreateToolbarGlyphFont()
    {
        try
        {
            return new Font("Segoe MDL2 Assets", _mPx.ToolbarGlyphFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        }
        catch (ArgumentException)
        {
            return CreateUiPixelFont("Segoe MDL2 Assets", _mPx.ToolbarGlyphFontSize, FontStyle.Regular);
        }
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

    private static void EnableDoubleBuffering(Control control)
    {
        typeof(Control)
            .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(control, true);
    }
}
