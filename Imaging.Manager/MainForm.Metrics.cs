using Shared.Shell.Interop;

namespace Imaging.Manager;

public partial class MainForm
{
    private int ScaleDip(int dip) => (int)Math.Round(dip * (DeviceDpi / 96f));
    private float ScaleFontPointToPx(float pointSize) => pointSize * (DeviceDpi / 72f);

    private void ReapplyDpiMetrics(DpiWindowResizeState? resizeState = null)
    {
        RecalcMetrics();
        RebuildFonts();
        ApplyMinimumSize(resizeState?.SuggestedBounds);
        ApplyScaledDpiWindowBounds(resizeState);
        ApplyLayoutMetrics();
        RefreshDiskImages();
        RefreshPartitionImages();
    }

    private void RecalcMetrics()
    {
        _mPx = ImagingManagerLayoutMetricsPx.FromDip(_mDip, ScaleDip, ScaleFontPointToPx);
    }

    private void RebuildFonts()
    {
        bool chromeChanged = _chromeFont == null || Math.Abs(_lastChromeFontPx - _mPx.ChromeFontSize) > 0.01f;
        bool detailChanged = _detailFont == null || Math.Abs(_lastDetailFontPx - _mPx.DetailFontSize) > 0.01f;
        if (!chromeChanged && !detailChanged)
            return;

        Font? oldChrome = null;
        Font? oldDetail = null;

        if (chromeChanged)
        {
            oldChrome = _chromeFont;
            _chromeFont = CreateUiPixelFont("Segoe UI", _mPx.ChromeFontSize, FontStyle.Regular);
            _lastChromeFontPx = _mPx.ChromeFontSize;
        }

        if (detailChanged)
        {
            oldDetail = _detailFont;
            _detailFont = CreateUiPixelFont("Consolas", _mPx.DetailFontSize, FontStyle.Regular);
            _lastDetailFontPx = _mPx.DetailFontSize;
        }

        ApplyChromeFonts();
        oldChrome?.Dispose();
        oldDetail?.Dispose();
    }

    private void ApplyChromeFonts()
    {
        if (_chromeFont == null || _detailFont == null)
            return;

        Font = _chromeFont;
        if (_pnlDisks is not null) _pnlDisks.Font = _chromeFont;
        if (_pnlPartitions is not null) _pnlPartitions.Font = _chromeFont;
        if (_rightPanel is not null) _rightPanel.Font = _chromeFont;
        if (_btnCapture is not null) _btnCapture.Font = _chromeFont;
        if (_btnApply is not null) _btnApply.Font = _chromeFont;
        if (_btnRefresh is not null) _btnRefresh.Font = _chromeFont;
        if (_btnMountWim is not null) _btnMountWim.Font = _chromeFont;
        if (_btnUnmountWim is not null) _btnUnmountWim.Font = _chromeFont;
        if (_btnCaptureWim is not null) _btnCaptureWim.Font = _chromeFont;
        if (_btnApplyWim is not null) _btnApplyWim.Font = _chromeFont;
        if (_btnExportWim is not null) _btnExportWim.Font = _chromeFont;
        if (_btnAddDrivers is not null) _btnAddDrivers.Font = _chromeFont;
        if (_btnUnlock is not null) _btnUnlock.Font = _chromeFont;
        if (_btnDeployWim is not null) _btnDeployWim.Font = _chromeFont;
        if (_lblStatus is not null) _lblStatus.Font = _chromeFont;
        if (_txtDiskStatus is not null) _txtDiskStatus.Font = _detailFont;

        ApplyChromeFontToChildren(_pnlDisks, _chromeFont);
        ApplyChromeFontToChildren(_pnlPartitions, _chromeFont);
    }

    private static void ApplyChromeFontToChildren(Control? parent, Font font)
    {
        if (parent == null || parent.IsDisposed)
            return;

        foreach (Control control in parent.Controls)
        {
            control.Font = font;
            foreach (Control child in control.Controls)
                child.Font = font;
        }
    }

    private void ApplyLayoutMetrics()
    {
        SuspendLayout();
        _splitMain?.SuspendLayout();
        _pnlDisks?.SuspendLayout();
        _pnlPartitions?.SuspendLayout();
        try
        {
            ApplyMinimumSize();

            if (_splitMain is { IsDisposed: false })
            {
                _splitMain.SplitterWidth = _mPx.SplitterWidth;
                int desiredLeft = GetDesiredDiskPaneWidth();
                int available = Math.Max(0, _splitMain.ClientSize.Width - _splitMain.SplitterWidth);
                int panel1Min = Math.Min(desiredLeft, available);
                int panel2Min = Math.Min(_mPx.DetailMinimumWidth, Math.Max(0, available - panel1Min));
                _splitMain.Panel1MinSize = panel1Min;
                _splitMain.Panel2MinSize = panel2Min;

                int maxDistance = Math.Max(0, _splitMain.ClientSize.Width - _splitMain.SplitterWidth - _splitMain.Panel2MinSize);
                if (maxDistance > 0)
                    _splitMain.SplitterDistance = Math.Min(desiredLeft, maxDistance);
            }

            if (_rightPanel is { IsDisposed: false })
                LayoutDiskDetails(_rightPanel);

            LayoutDiskTiles();
            LayoutPartitionTiles();
        }
        finally
        {
            _pnlPartitions?.ResumeLayout(true);
            _pnlDisks?.ResumeLayout(true);
            _splitMain?.ResumeLayout(true);
            ResumeLayout(true);
        }
    }

    private static void SetBoundsIfChanged(Control control, int x, int y, int width, int height)
    {
        Rectangle bounds = new(x, y, width, height);
        if (control.Bounds != bounds)
            control.Bounds = bounds;
    }

    private void TrackNormalClientSize()
    {
        if (WindowState == FormWindowState.Normal && ClientSize.Width > 0 && ClientSize.Height > 0)
            _lastNormalClientSize = ClientSize;
    }

    private Size GetTrackedNormalClientSize() =>
        _lastNormalClientSize.Width > 0 && _lastNormalClientSize.Height > 0 ? _lastNormalClientSize : ClientSize;

    private DpiWindowResizeState? CreateDpiWindowResizeState(DpiChangedEventArgs e)
    {
        if (WindowState == FormWindowState.Normal)
            return new DpiWindowResizeState(ClientSize, GetPreferredClientWidth(), e.DeviceDpiOld, e.DeviceDpiNew, e.SuggestedRectangle);

        if (WindowState != FormWindowState.Minimized)
            return null;

        Size normal = GetTrackedNormalClientSize();
        Rectangle restore = RestoreBounds;
        if (normal.Width <= 0 || normal.Height <= 0 || restore.Width <= 0 || restore.Height <= 0)
            return null;

        return new DpiWindowResizeState(normal, GetPreferredClientWidth(), e.DeviceDpiOld, e.DeviceDpiNew, restore);
    }

    private void ApplyScaledDpiWindowBounds(DpiWindowResizeState? state)
    {
        if (!state.HasValue)
            return;

        Rectangle target = GetScaledDpiWindowBounds(state.Value);
        if (WindowState == FormWindowState.Minimized)
        {
            TrySetMinimizedRestoreBounds(target);
            return;
        }

        if (WindowState == FormWindowState.Normal)
        {
            Bounds = target;
            TrackNormalClientSize();
        }
    }

    private Rectangle GetScaledDpiWindowBounds(DpiWindowResizeState state)
    {
        int oldDpi = Math.Max(1, state.OldDpi);
        int newDpi = Math.Max(1, state.NewDpi);
        int width = (int)Math.Round(state.OldClientSize.Width * (newDpi / (double)oldDpi));
        int height = (int)Math.Round(state.OldClientSize.Height * (newDpi / (double)oldDpi));

        int tolerance = Math.Max(3, (int)Math.Round(8 * (oldDpi / 96d)));
        if (Math.Abs(state.OldClientSize.Width - state.OldPreferredClientWidth) <= tolerance)
            width = GetPreferredClientWidth();

        Size windowSize = SizeFromClientSize(new Size(Math.Max(1, width), Math.Max(1, height)));
        windowSize.Width = Math.Max(windowSize.Width, MinimumSize.Width);
        windowSize.Height = Math.Max(windowSize.Height, MinimumSize.Height);
        return FitBoundsToAvailableArea(new Rectangle(state.SuggestedBounds.Location, windowSize));
    }

    private bool TrySetMinimizedRestoreBounds(Rectangle bounds)
    {
        if (IsDisposed || !IsHandleCreated || WindowState != FormWindowState.Minimized)
            return false;

        User32.WINDOWPLACEMENT placement = new()
        {
            length = System.Runtime.InteropServices.Marshal.SizeOf<User32.WINDOWPLACEMENT>()
        };

        if (!User32.GetWindowPlacement(Handle, ref placement))
            return false;

        placement.rcNormalPosition = new User32.RECT
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Right = bounds.Right,
            Bottom = bounds.Bottom
        };

        return User32.SetWindowPlacement(Handle, ref placement);
    }

    private void ApplyMinimumSize(Rectangle? preferredBounds = null)
    {
        Size minimum = SizeFromClientSize(new Size(GetMinimumClientWidth(), _mPx.InitialClientHeight));
        Rectangle available = GetAvailableBounds(preferredBounds ?? Bounds);
        if (available.Width > 0) minimum.Width = Math.Min(minimum.Width, available.Width);
        if (available.Height > 0) minimum.Height = Math.Min(minimum.Height, available.Height);
        MinimumSize = minimum;
    }

    private int GetMinimumClientWidth() => GetDesiredDiskPaneWidth() + _mPx.SplitterWidth + _mPx.DetailMinimumWidth;
    private int GetPreferredClientWidth() => GetMinimumClientWidth();

    private Rectangle GetAvailableBounds(Rectangle bounds)
    {
        Rectangle work = Screen.FromRectangle(bounds).WorkingArea;
        int gap = Math.Max(4, ScaleDip(8));
        Rectangle available = Rectangle.Inflate(work, -gap, -gap);
        return available.Width > 0 && available.Height > 0 ? available : work;
    }

    private Rectangle FitBoundsToAvailableArea(Rectangle bounds)
    {
        Rectangle available = GetAvailableBounds(bounds);
        int width = Math.Min(bounds.Width, available.Width);
        int height = Math.Min(bounds.Height, available.Height);
        int x = Math.Min(Math.Max(bounds.X, available.Left), available.Right - width);
        int y = Math.Min(Math.Max(bounds.Y, available.Top), available.Bottom - height);
        return new Rectangle(x, y, width, height);
    }

    private static Font CreateUiPixelFont(string familyName, float size, FontStyle style)
    {
        float safe = size > 0f ? size : 12f;
        try { return new Font(familyName, safe, style, GraphicsUnit.Pixel); }
        catch (ArgumentException) { return new Font(FontFamily.GenericSansSerif, safe, FontStyle.Regular, GraphicsUnit.Pixel); }
    }

    private readonly struct DpiWindowResizeState
    {
        public DpiWindowResizeState(Size oldClientSize, int oldPreferredClientWidth, int oldDpi, int newDpi, Rectangle suggestedBounds)
        {
            OldClientSize = oldClientSize;
            OldPreferredClientWidth = oldPreferredClientWidth;
            OldDpi = oldDpi;
            NewDpi = newDpi;
            SuggestedBounds = suggestedBounds;
        }

        public readonly Size OldClientSize;
        public readonly int OldPreferredClientWidth;
        public readonly int OldDpi;
        public readonly int NewDpi;
        public readonly Rectangle SuggestedBounds;
    }

    private sealed class ImagingManagerLayoutMetrics
    {
        // Keep the width geometry aligned with BitLocker.Manager. The 412-DIP
        // client height also fits four 94-DIP disk tiles without a vertical
        // scrollbar while leaving a little resize breathing room.
        public int InitialClientWidthDip { get; init; } = 610;
        public int InitialClientHeightDip { get; init; } = 412;
        public int DiskPaneDefaultWidthDip { get; init; } = 128;
        public int DiskPaneMinimumWidthDip { get; init; } = 112;
        public int DiskPaneMaximumWidthDip { get; init; } = 180;
        public int DiskPaneScrollBarAllowanceDip { get; init; } = 17;
        public int DiskPaneBorderAllowanceDip { get; init; } = 4;
        public int SplitterWidthPx { get; init; } = 1;
        public int DetailMarginDip { get; init; } = 12;
        public int DetailGapDip { get; init; } = 8;
        public int DetailButtonGapDip { get; init; } = 5;
        public int DetailButtonWidthDip { get; init; } = 140;
        public int DetailButtonHeightDip { get; init; } = 30;
        public int DetailStatusHeightDip { get; init; } = 20;
        public int DetailContentMinimumWidthDip { get; init; } = 340;

        public int DiskTileHeightDip { get; init; } = 94;
        public int DiskTileIconTopDip { get; init; } = 8;
        public int DiskTileIconSizeDip { get; init; } = 32;
        public int DiskTileNameTopDip { get; init; } = 43;
        public int DiskTileNameHeightDip { get; init; } = 20;
        public int DiskTileSubTopDip { get; init; } = 63;
        public int DiskTileSubHeightDip { get; init; } = 24;
        public int DiskTilePadXDip { get; init; } = 6;

        // Horizontal partition selector at the top of the selected disk pane.
        // Height includes enough room for a horizontal scroll bar without
        // forcing a vertical bar when many partitions are present.
        public int PartitionPaneHeightDip { get; init; } = 94;
        public int PartitionTileWidthDip { get; init; } = 112;
        public int PartitionTileHeightDip { get; init; } = 72;
        public int PartitionTileIconTopDip { get; init; } = 5;
        public int PartitionTileIconSizeDip { get; init; } = 28;
        public int PartitionTileNameTopDip { get; init; } = 36;
        public int PartitionTileNameHeightDip { get; init; } = 18;
        public int PartitionTileSubTopDip { get; init; } = 53;
        public int PartitionTileSubHeightDip { get; init; } = 17;
        public int PartitionTilePadXDip { get; init; } = 5;

        public float ChromeFontSizePt { get; init; } = 9f;
        public float DetailFontSizePt { get; init; } = 9f;
    }

    private sealed class ImagingManagerLayoutMetricsPx
    {
        public int InitialClientWidth { get; init; }
        public int InitialClientHeight { get; init; }
        public int LeftPanelWidth { get; init; }
        public int DiskPaneMinimumWidth { get; init; }
        public int DiskPaneMaximumWidth { get; init; }
        public int DiskPaneScrollBarAllowance { get; init; }
        public int DiskPaneBorderAllowance { get; init; }
        public int SplitterWidth { get; init; }
        public int DetailMargin { get; init; }
        public int DetailGap { get; init; }
        public int DetailButtonGap { get; init; }
        public int DetailButtonWidth { get; init; }
        public int DetailButtonHeight { get; init; }
        public int DetailStatusHeight { get; init; }
        public int DetailMinimumWidth { get; init; }
        public int DiskTileHeight { get; init; }
        public int DiskTileIconTop { get; init; }
        public int DiskTileIconSize { get; init; }
        public int DiskTileNameTop { get; init; }
        public int DiskTileNameHeight { get; init; }
        public int DiskTileSubTop { get; init; }
        public int DiskTileSubHeight { get; init; }
        public int DiskTilePadX { get; init; }
        public int PartitionPaneHeight { get; init; }
        public int PartitionTileWidth { get; init; }
        public int PartitionTileHeight { get; init; }
        public int PartitionTileIconTop { get; init; }
        public int PartitionTileIconSize { get; init; }
        public int PartitionTileNameTop { get; init; }
        public int PartitionTileNameHeight { get; init; }
        public int PartitionTileSubTop { get; init; }
        public int PartitionTileSubHeight { get; init; }
        public int PartitionTilePadX { get; init; }
        public float ChromeFontSize { get; init; }
        public float DetailFontSize { get; init; }

        public static ImagingManagerLayoutMetricsPx FromDip(ImagingManagerLayoutMetrics dip, Func<int, int> scale, Func<float, float> scaleFont)
        {
            int margin = scale(dip.DetailMarginDip);
            int gap = scale(dip.DetailGapDip);
            int buttonGap = scale(dip.DetailButtonGapDip);
            int buttonWidth = scale(dip.DetailButtonWidthDip);
            int buttonHeight = scale(dip.DetailButtonHeightDip);
            int statusHeight = scale(dip.DetailStatusHeightDip);
            int paneMin = scale(dip.DiskPaneMinimumWidthDip);
            int paneMax = Math.Max(paneMin, scale(dip.DiskPaneMaximumWidthDip));
            int paneDefault = Math.Clamp(scale(dip.DiskPaneDefaultWidthDip), paneMin, paneMax);
            // The detail pane is now a content column plus a fixed action column.
            // Keep the content column wide enough for three 112-DIP partition tiles
            // without a horizontal scrollbar at the default window size. The
            // action column is slightly narrower to offset most of that growth.
            int contentMin = scale(dip.DetailContentMinimumWidthDip);
            int detailMin = (margin * 3) + contentMin + buttonWidth;

            return new ImagingManagerLayoutMetricsPx
            {
                InitialClientWidth = Math.Max(scale(dip.InitialClientWidthDip), paneDefault + dip.SplitterWidthPx + detailMin),
                InitialClientHeight = scale(dip.InitialClientHeightDip),
                LeftPanelWidth = paneDefault,
                DiskPaneMinimumWidth = paneMin,
                DiskPaneMaximumWidth = paneMax,
                DiskPaneScrollBarAllowance = Math.Max(SystemInformation.VerticalScrollBarWidth, scale(dip.DiskPaneScrollBarAllowanceDip)),
                DiskPaneBorderAllowance = scale(dip.DiskPaneBorderAllowanceDip),
                SplitterWidth = dip.SplitterWidthPx,
                DetailMargin = margin,
                DetailGap = gap,
                DetailButtonGap = buttonGap,
                DetailButtonWidth = buttonWidth,
                DetailButtonHeight = buttonHeight,
                DetailStatusHeight = statusHeight,
                DetailMinimumWidth = detailMin,
                DiskTileHeight = scale(dip.DiskTileHeightDip),
                DiskTileIconTop = scale(dip.DiskTileIconTopDip),
                DiskTileIconSize = scale(dip.DiskTileIconSizeDip),
                DiskTileNameTop = scale(dip.DiskTileNameTopDip),
                DiskTileNameHeight = scale(dip.DiskTileNameHeightDip),
                DiskTileSubTop = scale(dip.DiskTileSubTopDip),
                DiskTileSubHeight = scale(dip.DiskTileSubHeightDip),
                DiskTilePadX = scale(dip.DiskTilePadXDip),
                PartitionPaneHeight = scale(dip.PartitionPaneHeightDip),
                PartitionTileWidth = scale(dip.PartitionTileWidthDip),
                PartitionTileHeight = scale(dip.PartitionTileHeightDip),
                PartitionTileIconTop = scale(dip.PartitionTileIconTopDip),
                PartitionTileIconSize = scale(dip.PartitionTileIconSizeDip),
                PartitionTileNameTop = scale(dip.PartitionTileNameTopDip),
                PartitionTileNameHeight = scale(dip.PartitionTileNameHeightDip),
                PartitionTileSubTop = scale(dip.PartitionTileSubTopDip),
                PartitionTileSubHeight = scale(dip.PartitionTileSubHeightDip),
                PartitionTilePadX = scale(dip.PartitionTilePadXDip),
                ChromeFontSize = scaleFont(dip.ChromeFontSizePt),
                DetailFontSize = scaleFont(dip.DetailFontSizePt)
            };
        }
    }
}
