using Shared.Shell.Interop;

namespace BitLocker.Manager;

public partial class MainForm
{
    // DPI entry points

    private int ScaleDip(int dip) => (int)Math.Round(dip * (DeviceDpi / 96f));
    private float ScaleFontPointToPx(float pointSize) => pointSize * (DeviceDpi / 72f);

    private void ReapplyDpiMetrics(DpiWindowResizeState? resizeState = null)
    {
        RecalcMetrics();
        RebuildFonts();
        ApplyBitLockerMinimumSize(resizeState?.SuggestedBounds);
        ApplyScaledDpiWindowBounds(resizeState);
        ApplyLayoutMetrics();
        RefreshDriveImages();
    }

    private void RecalcMetrics()
    {
        _mPx = BitLockerManagerLayoutMetricsPx.FromDip(_mDip, ScaleDip, ScaleFontPointToPx);
    }

    private void RebuildFonts()
    {
        bool chromeChanged = _chromeFont == null || Math.Abs(_lastChromeFontPx - _mPx.ChromeFontSize) > 0.01f;
        bool statusChanged = _statusFont == null || Math.Abs(_lastStatusFontPx - _mPx.StatusFontSize) > 0.01f;

        if (!chromeChanged && !statusChanged)
            return;

        Font? oldChromeFont = null;
        Font? oldStatusFont = null;

        if (chromeChanged)
        {
            oldChromeFont = _chromeFont;
            _chromeFont = CreateUiPixelFont("Segoe UI", _mPx.ChromeFontSize, FontStyle.Regular);
            _lastChromeFontPx = _mPx.ChromeFontSize;
        }

        if (statusChanged)
        {
            oldStatusFont = _statusFont;
            _statusFont = CreateUiPixelFont("Consolas", _mPx.StatusFontSize, FontStyle.Regular);
            _lastStatusFontPx = _mPx.StatusFontSize;
        }

        ApplyChromeFonts();

        oldChromeFont?.Dispose();
        oldStatusFont?.Dispose();
    }

    private void ApplyLayoutMetrics()
    {
        SuspendLayout();
        _splitMain?.SuspendLayout();
        _pnlVolumes?.SuspendLayout();

        try
        {
            ApplyBitLockerMinimumSize();

            if (_splitMain is { IsDisposed: false })
            {
                if (_splitMain.SplitterWidth != _mPx.SplitterWidth)
                    _splitMain.SplitterWidth = _mPx.SplitterWidth;

                int desiredLeftPanelWidth = GetDesiredVolumePaneWidth();
                int availablePaneWidth = Math.Max(0, _splitMain.ClientSize.Width - _splitMain.SplitterWidth);
                int panel1MinSize = Math.Min(desiredLeftPanelWidth, availablePaneWidth);
                int panel2MinSize = Math.Min(
                    _mPx.DetailMinimumWidth,
                    Math.Max(0, availablePaneWidth - panel1MinSize));

                if (_splitMain.Panel1MinSize != panel1MinSize)
                    _splitMain.Panel1MinSize = panel1MinSize;

                if (_splitMain.Panel2MinSize != panel2MinSize)
                    _splitMain.Panel2MinSize = panel2MinSize;

                int maxSplitterDistance = Math.Max(
                    0,
                    _splitMain.ClientSize.Width - _splitMain.SplitterWidth - _splitMain.Panel2MinSize);

                if (maxSplitterDistance > 0)
                {
                    int splitterDistance = Math.Min(desiredLeftPanelWidth, maxSplitterDistance);
                    if (_splitMain.SplitterDistance != splitterDistance)
                        _splitMain.SplitterDistance = splitterDistance;
                }
            }

            if (_rightPanel is { IsDisposed: false })
                LayoutVolumeDetails(_rightPanel);

            LayoutVolumeTiles();
        }
        finally
        {
            _pnlVolumes?.ResumeLayout(true);
            _splitMain?.ResumeLayout(true);
            ResumeLayout(true);
        }
    }

    // Fonts and control layout

    private void ApplyChromeFonts()
    {
        if (_chromeFont == null || _statusFont == null)
            return;

        if (!ReferenceEquals(Font, _chromeFont))
            Font = _chromeFont;

        ApplyFont(_pnlVolumes, _chromeFont);
        ApplyFont(_rightPanel, _chromeFont);
        ApplyFont(_lblStatus, _chromeFont);
        ApplyFont(_btnUnlock, _chromeFont);
        ApplyFont(_btnLock, _chromeFont);
        ApplyFont(_btnRefresh, _chromeFont);
        ApplyFont(_txtVolumeStatus, _statusFont);

        if (_pnlVolumes == null || _pnlVolumes.IsDisposed)
            return;

        foreach (Control control in _pnlVolumes.Controls)
        {
            if (control is not Panel tile)
                continue;

            ApplyFont(tile, _chromeFont);

            foreach (Control child in tile.Controls)
                ApplyFont(child, _chromeFont);
        }
    }

    private static void ApplyFont(Control? control, Font font)
    {
        if (control == null || control.IsDisposed)
            return;

        if (!ReferenceEquals(control.Font, font))
            control.Font = font;
    }

    private static void SetBoundsIfChanged(Control control, int x, int y, int width, int height)
    {
        if (control == null || control.IsDisposed)
            return;

        Rectangle bounds = new(x, y, width, height);

        if (control.Bounds != bounds)
            control.Bounds = bounds;
    }

    private static void SetTextIfChanged(Control control, string text)
    {
        if (control == null || control.IsDisposed)
            return;

        text ??= string.Empty;

        if (!string.Equals(control.Text, text, StringComparison.Ordinal))
            control.Text = text;
    }

    private static void SetEnabledIfChanged(Control control, bool enabled)
    {
        if (control == null || control.IsDisposed)
            return;

        if (control.Enabled != enabled)
            control.Enabled = enabled;
    }

    private static void SetVisibleIfChanged(Control control, bool visible)
    {
        if (control == null || control.IsDisposed)
            return;

        if (control.Visible != visible)
            control.Visible = visible;
    }

    private void RefreshDriveImages()
    {
        if (_pnlVolumes == null || _pnlVolumes.IsDisposed)
            return;

        foreach (Control control in _pnlVolumes.Controls)
        {
            if (control is not Panel tile || tile.Tag is not BitLocker.Core.BitLockerVolumeInfo volume)
                continue;

            foreach (Control child in tile.Controls)
            {
                if (child is PictureBox picture)
                {
                    Image desiredImage = GetDriveImage(GetDriveVisualKind(volume));
                    if (!ReferenceEquals(picture.Image, desiredImage))
                        picture.Image = desiredImage;

                    break;
                }
            }
        }
    }

    private void TrackNormalClientSize()
    {
        if (WindowState != FormWindowState.Normal ||
            ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        _lastNormalClientSize = ClientSize;
    }

    private Size GetTrackedNormalClientSize()
    {
        if (_lastNormalClientSize.Width > 0 && _lastNormalClientSize.Height > 0)
            return _lastNormalClientSize;

        return ClientSize.Width > 0 && ClientSize.Height > 0
            ? ClientSize
            : Size.Empty;
    }

    // Window sizing across DPI changes

    private DpiWindowResizeState? CreateDpiWindowResizeState(DpiChangedEventArgs e)
    {
        if (WindowState == FormWindowState.Normal)
        {
            return new DpiWindowResizeState(
                ClientSize,
                GetBitLockerPreferredClientWidth(),
                e.DeviceDpiOld,
                e.DeviceDpiNew,
                e.SuggestedRectangle);
        }

        if (WindowState != FormWindowState.Minimized)
            return null;

        Size normalClientSize = GetTrackedNormalClientSize();
        Rectangle restoreBounds = RestoreBounds;

        if (normalClientSize.Width <= 0 || normalClientSize.Height <= 0 ||
            restoreBounds.Width <= 0 || restoreBounds.Height <= 0)
        {
            return null;
        }

        return new DpiWindowResizeState(
            normalClientSize,
            GetBitLockerPreferredClientWidth(),
            e.DeviceDpiOld,
            e.DeviceDpiNew,
            restoreBounds);
    }

    private void ApplyScaledDpiWindowBounds(DpiWindowResizeState? resizeState)
    {
        if (!resizeState.HasValue)
            return;

        Rectangle targetBounds = GetScaledDpiWindowBounds(resizeState.Value);

        if (WindowState == FormWindowState.Minimized)
        {
            TrySetMinimizedRestoreBounds(targetBounds);
            return;
        }

        if (WindowState != FormWindowState.Normal)
            return;

        if (Bounds != targetBounds)
            Bounds = targetBounds;

        TrackNormalClientSize();
    }

    private Rectangle GetScaledDpiWindowBounds(DpiWindowResizeState state)
    {
        Size targetClientSize = GetScaledDpiClientSize(state);

        Size targetWindowSize = SizeFromClientSize(targetClientSize);
        targetWindowSize.Width = Math.Max(targetWindowSize.Width, MinimumSize.Width);
        targetWindowSize.Height = Math.Max(targetWindowSize.Height, MinimumSize.Height);

        Rectangle targetBounds = new(state.SuggestedBounds.Location, targetWindowSize);

        return FitBoundsToDpiAvailableArea(targetBounds);
    }

    private Size GetScaledDpiClientSize(DpiWindowResizeState state)
    {
        int oldDpi = Math.Max(1, state.OldDpi);
        int newDpi = Math.Max(1, state.NewDpi);

        int targetWidth = Math.Max(
            1,
            (int)Math.Round(state.OldClientSize.Width * (newDpi / (double)oldDpi)));
        int targetHeight = Math.Max(
            1,
            (int)Math.Round(state.OldClientSize.Height * (newDpi / (double)oldDpi)));

        if (IsNearDpiPreferredWidth(state))
            targetWidth = GetBitLockerPreferredClientWidth();

        return new Size(targetWidth, targetHeight);
    }

    private static bool IsNearDpiPreferredWidth(DpiWindowResizeState state)
    {
        if (state.OldPreferredClientWidth <= 0)
            return false;

        int tolerance = Math.Max(3, (int)Math.Round(8 * (Math.Max(1, state.OldDpi) / 96d)));
        return Math.Abs(state.OldClientSize.Width - state.OldPreferredClientWidth) <= tolerance;
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

    private Rectangle FitBoundsToDpiAvailableArea(Rectangle bounds)
    {
        Rectangle availableBounds = GetDpiAvailableBounds(bounds);

        if (availableBounds.Width <= 0 || availableBounds.Height <= 0)
            return bounds;

        int width = Math.Min(bounds.Width, availableBounds.Width);
        int height = Math.Min(bounds.Height, availableBounds.Height);

        int x = Math.Min(
            Math.Max(bounds.X, availableBounds.Left),
            availableBounds.Right - width);

        int y = Math.Min(
            Math.Max(bounds.Y, availableBounds.Top),
            availableBounds.Bottom - height);

        return new Rectangle(x, y, width, height);
    }

    private void ApplyBitLockerMinimumSize(Rectangle? preferredBounds = null)
    {
        Size minimumSize = GetBitLockerMinimumWindowSize();
        Rectangle availableBounds = GetDpiAvailableBounds(preferredBounds ?? Bounds);

        if (availableBounds.Width > 0)
            minimumSize.Width = Math.Min(minimumSize.Width, availableBounds.Width);

        if (availableBounds.Height > 0)
            minimumSize.Height = Math.Min(minimumSize.Height, availableBounds.Height);

        if (MinimumSize != minimumSize)
            MinimumSize = minimumSize;
    }

    private Size GetBitLockerMinimumWindowSize()
    {
        return SizeFromClientSize(new Size(GetBitLockerMinimumClientWidth(), _mPx.InitialClientHeight));
    }

    private int GetBitLockerMinimumClientWidth()
    {
        return GetDesiredVolumePaneWidth() + _mPx.SplitterWidth + _mPx.DetailMinimumWidth;
    }

    private int GetBitLockerPreferredClientWidth()
    {
        return GetBitLockerMinimumClientWidth();
    }

    private Rectangle GetDpiAvailableBounds(Rectangle bounds)
    {
        Rectangle workingArea = Screen.FromRectangle(bounds).WorkingArea;

        // Keep the same small visual gap used by the Explorer DPI sizing path
        // when a scaled window would otherwise touch the desktop edges.
        int gap = Math.Max(4, ScaleDip(8));
        Rectangle availableBounds = Rectangle.Inflate(workingArea, -gap, -gap);

        return availableBounds.Width > 0 && availableBounds.Height > 0
            ? availableBounds
            : workingArea;
    }

    private readonly struct DpiWindowResizeState
    {
        public DpiWindowResizeState(
            Size oldClientSize,
            int oldPreferredClientWidth,
            int oldDpi,
            int newDpi,
            Rectangle suggestedBounds)
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

    // Base DIP values and scaled pixel values are kept separate so the same
    // layout rules can be recalculated cleanly whenever DeviceDpi changes.
    private sealed class BitLockerManagerLayoutMetrics
    {
        public int InitialClientWidthDip { get; init; } = 610;
        public int InitialClientHeightDip { get; init; } = 370;

        public int VolumePaneDefaultWidthDip { get; init; } = 128;
        public int VolumePaneMinimumWidthDip { get; init; } = 112;
        public int VolumePaneMaximumWidthDip { get; init; } = 180;
        public int VolumePaneScrollBarAllowanceDip { get; init; } = 17;
        public int VolumePaneBorderAllowanceDip { get; init; } = 4;
        public int SplitterWidthPx { get; init; } = 1;

        public int DetailMarginDip { get; init; } = 12;
        public int DetailGapDip { get; init; } = 8;
        public int DetailButtonWidthDip { get; init; } = 150;
        public int DetailButtonHeightDip { get; init; } = 30;
        public int DetailStatusHeightDip { get; init; } = 20;

        public int VolumeTileHeightDip { get; init; } = 82;
        public int VolumeTileNamePadXDip { get; init; } = 6;
        public int VolumeTileIconTopDip { get; init; } = 8;
        public int VolumeTileIconSizeDip { get; init; } = 32;
        public int VolumeTileNameTopDip { get; init; } = 44;
        public int VolumeTileNameHeightDip { get; init; } = 30;
        public int MinimumVolumeTileWidthDip { get; init; } = 32;

        public float ChromeFontSizePt { get; init; } = 9f;
        public float StatusFontSizePt { get; init; } = 9f;
    }

    private sealed class BitLockerManagerLayoutMetricsPx
    {
        public int InitialClientWidth { get; init; }
        public int InitialClientHeight { get; init; }

        public int LeftPanelWidth { get; init; }
        public int VolumePaneMinimumWidth { get; init; }
        public int VolumePaneMaximumWidth { get; init; }
        public int VolumePaneScrollBarAllowance { get; init; }
        public int VolumePaneBorderAllowance { get; init; }
        public int SplitterWidth { get; init; }

        public int DetailMargin { get; init; }
        public int DetailGap { get; init; }
        public int DetailButtonWidth { get; init; }
        public int DetailButtonHeight { get; init; }
        public int DetailStatusHeight { get; init; }
        public int DetailMinimumWidth { get; init; }

        public int VolumeTileHeight { get; init; }
        public int VolumeTileNamePadX { get; init; }
        public int VolumeTileIconTop { get; init; }
        public int VolumeTileIconSize { get; init; }
        public int VolumeTileNameTop { get; init; }
        public int VolumeTileNameHeight { get; init; }
        public int MinimumVolumeTileWidth { get; init; }

        public float ChromeFontSize { get; init; }
        public float StatusFontSize { get; init; }

        public static BitLockerManagerLayoutMetricsPx FromDip(
            BitLockerManagerLayoutMetrics dip,
            Func<int, int> scale,
            Func<float, float> scaleFontPointToPx)
        {
            int detailMargin = scale(dip.DetailMarginDip);
            int detailGap = scale(dip.DetailGapDip);
            int detailButtonWidth = scale(dip.DetailButtonWidthDip);
            int detailButtonHeight = scale(dip.DetailButtonHeightDip);
            int detailStatusHeight = scale(dip.DetailStatusHeightDip);

            int volumeTileIconSize = scale(dip.VolumeTileIconSizeDip);
            int volumePaneMinimumWidth = scale(dip.VolumePaneMinimumWidthDip);
            int volumePaneMaximumWidth = Math.Max(
                volumePaneMinimumWidth,
                scale(dip.VolumePaneMaximumWidthDip));
            int volumePaneDefaultWidth = Math.Clamp(
                scale(dip.VolumePaneDefaultWidthDip),
                volumePaneMinimumWidth,
                volumePaneMaximumWidth);
            int volumeScrollBarAllowance = Math.Max(
                SystemInformation.VerticalScrollBarWidth,
                scale(dip.VolumePaneScrollBarAllowanceDip));
            int volumeBorderAllowance = scale(dip.VolumePaneBorderAllowanceDip);

            int detailMinimumWidth =
                (detailMargin * 2) +
                (detailButtonWidth * 3) +
                (detailGap * 2);

            int minimumClientWidth = volumePaneDefaultWidth + dip.SplitterWidthPx + detailMinimumWidth;

            return new BitLockerManagerLayoutMetricsPx
            {
                InitialClientWidth = Math.Max(scale(dip.InitialClientWidthDip), minimumClientWidth),
                InitialClientHeight = scale(dip.InitialClientHeightDip),

                LeftPanelWidth = volumePaneDefaultWidth,
                VolumePaneMinimumWidth = volumePaneMinimumWidth,
                VolumePaneMaximumWidth = volumePaneMaximumWidth,
                VolumePaneScrollBarAllowance = volumeScrollBarAllowance,
                VolumePaneBorderAllowance = volumeBorderAllowance,
                SplitterWidth = dip.SplitterWidthPx,

                DetailMargin = detailMargin,
                DetailGap = detailGap,
                DetailButtonWidth = detailButtonWidth,
                DetailButtonHeight = detailButtonHeight,
                DetailStatusHeight = detailStatusHeight,
                DetailMinimumWidth = detailMinimumWidth,

                VolumeTileHeight = scale(dip.VolumeTileHeightDip),
                VolumeTileNamePadX = scale(dip.VolumeTileNamePadXDip),
                VolumeTileIconTop = scale(dip.VolumeTileIconTopDip),
                VolumeTileIconSize = volumeTileIconSize,
                VolumeTileNameTop = scale(dip.VolumeTileNameTopDip),
                VolumeTileNameHeight = scale(dip.VolumeTileNameHeightDip),
                MinimumVolumeTileWidth = Math.Max(scale(dip.MinimumVolumeTileWidthDip), volumePaneMinimumWidth - volumeBorderAllowance - volumeScrollBarAllowance),

                ChromeFontSize = scaleFontPointToPx(dip.ChromeFontSizePt),
                StatusFontSize = scaleFontPointToPx(dip.StatusFontSizePt)
            };
        }
    }
}
