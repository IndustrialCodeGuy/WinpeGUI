namespace BitLocker.Unlock;

public sealed partial class UnlockForm
{
    // DPI entry points

    private int ScaleDip(int dip) => (int)Math.Round(dip * (DeviceDpi / 96f));
    private float ScaleFontPointToPx(float pointSize) => pointSize * (DeviceDpi / 72f);

    private void ReapplyDpiMetrics()
    {
        RecalcMetrics();
        RebuildFonts();
        ApplyLayoutMetrics();
    }

    private void RecalcMetrics()
    {
        _mPx = UnlockLayoutMetricsPx.FromDip(_mDip, ScaleDip, ScaleFontPointToPx);
    }

    private void RebuildFonts()
    {
        if (_chromeFont != null && Math.Abs(_lastChromeFontPx - _mPx.ChromeFontSize) <= 0.01f)
            return;

        Font chromeFont = CreateUiPixelFont("Segoe UI", _mPx.ChromeFontSize, FontStyle.Regular);
        Font? oldChromeFont = _chromeFont;

        _chromeFont = chromeFont;
        _lastChromeFontPx = _mPx.ChromeFontSize;

        ApplyChromeFonts();

        oldChromeFont?.Dispose();
    }

    private void ApplyLayoutMetrics()
    {
        SuspendLayout();
        try
        {
            Size clientSize = new(_mPx.ClientWidth, _mPx.ClientHeight);
            if (ClientSize != clientSize)
                ClientSize = clientSize;

            LayoutUnlockControls();
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    // Fonts and control layout

    private void ApplyChromeFonts()
    {
        if (_chromeFont == null)
            return;

        if (!ReferenceEquals(Font, _chromeFont))
            Font = _chromeFont;

        foreach (Control control in Controls)
        {
            if (!ReferenceEquals(control.Font, _chromeFont))
                control.Font = _chromeFont;
        }
    }

    private void LayoutUnlockControls()
    {
        if (_lblPrompt == null || _txtSecret == null)
            return;

        SetBoundsIfChanged(
            _lblPrompt,
            _mPx.Margin,
            _mPx.PromptTop,
            _mPx.ContentWidth,
            _mPx.PromptHeight);

        SetBoundsIfChanged(
            _txtSecret,
            _mPx.Margin,
            _mPx.SecretTop,
            _mPx.ContentWidth,
            _txtSecret.Height);

        SetBoundsIfChanged(
            _lnkRecoveryPassword,
            _mPx.Margin,
            _mPx.LinkTop,
            _mPx.RecoveryPasswordLinkWidth,
            _mPx.LinkHeight);

        SetBoundsIfChanged(
            _lnkRecoveryKeyFile,
            _mPx.RecoveryKeyFileLinkLeft,
            _mPx.LinkTop,
            _mPx.RecoveryKeyFileLinkWidth,
            _mPx.LinkHeight);

        SetBoundsIfChanged(
            _lblRecoveryKeyId,
            _mPx.Margin,
            _mPx.RecoveryKeyIdTop,
            _mPx.RecoveryKeyIdWidth,
            _mPx.RecoveryKeyIdHeight);

        SetBoundsIfChanged(
            _btnUnlock,
            _mPx.UnlockButtonLeft,
            _mPx.ButtonTop,
            _mPx.ButtonWidth,
            _mPx.ButtonHeight);

        SetBoundsIfChanged(
            _btnCancel,
            _mPx.CancelButtonLeft,
            _mPx.ButtonTop,
            _mPx.ButtonWidth,
            _mPx.ButtonHeight);
    }

    private static void SetBoundsIfChanged(Control control, int x, int y, int width, int height)
    {
        Rectangle bounds = new(x, y, width, height);

        if (control.Bounds != bounds)
            control.Bounds = bounds;
    }

    private static void SetTextIfChanged(Control control, string text)
    {
        text ??= string.Empty;

        if (!string.Equals(control.Text, text, StringComparison.Ordinal))
            control.Text = text;
    }

    private static void SetVisibleIfChanged(Control control, bool visible)
    {
        if (control.Visible != visible)
            control.Visible = visible;
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

    // Base DIP values and scaled pixel values are kept separate so the fixed
    // dialog can be recalculated cleanly whenever DeviceDpi changes.
    private sealed class UnlockLayoutMetrics
    {
        public int ClientWidthDip { get; init; } = 347;
        public int ClientHeightDip { get; init; } = 160;

        public int MarginDip { get; init; } = 12;
        public int PromptTopDip { get; init; } = 16;
        public int PromptHeightDip { get; init; } = 24;
        public int SecretTopDip { get; init; } = 44;
        public int LinkTopDip { get; init; } = 76;
        public int LinkHeightDip { get; init; } = 22;

        public int RecoveryPasswordLinkWidthDip { get; init; } = 160;
        public int RecoveryKeyFileLinkLeftDip { get; init; } = 180;
        public int RecoveryKeyFileLinkWidthDip { get; init; } = 155;

        public int RecoveryKeyIdTopDip { get; init; } = 118;
        public int RecoveryKeyIdWidthDip { get; init; } = 160;
        public int RecoveryKeyIdHeightDip { get; init; } = 28;

        public int ButtonTopDip { get; init; } = 118;
        public int ButtonWidthDip { get; init; } = 75;
        public int ButtonHeightDip { get; init; } = 28;
        public int ButtonGapDip { get; init; } = 5;

        public float ChromeFontSizePt { get; init; } = 9f;
    }

    private sealed class UnlockLayoutMetricsPx
    {
        public int ClientWidth { get; init; }
        public int ClientHeight { get; init; }

        public int Margin { get; init; }
        public int ContentWidth { get; init; }
        public int PromptTop { get; init; }
        public int PromptHeight { get; init; }
        public int SecretTop { get; init; }
        public int LinkTop { get; init; }
        public int LinkHeight { get; init; }

        public int RecoveryPasswordLinkWidth { get; init; }
        public int RecoveryKeyFileLinkLeft { get; init; }
        public int RecoveryKeyFileLinkWidth { get; init; }

        public int RecoveryKeyIdTop { get; init; }
        public int RecoveryKeyIdWidth { get; init; }
        public int RecoveryKeyIdHeight { get; init; }

        public int ButtonTop { get; init; }
        public int ButtonWidth { get; init; }
        public int ButtonHeight { get; init; }
        public int UnlockButtonLeft { get; init; }
        public int CancelButtonLeft { get; init; }

        public float ChromeFontSize { get; init; }

        public static UnlockLayoutMetricsPx FromDip(
            UnlockLayoutMetrics dip,
            Func<int, int> scale,
            Func<float, float> scaleFontPointToPx)
        {
            int clientWidth = scale(dip.ClientWidthDip);
            int margin = scale(dip.MarginDip);
            int buttonWidth = scale(dip.ButtonWidthDip);
            int buttonGap = scale(dip.ButtonGapDip);
            int unlockButtonLeft = clientWidth - margin - (buttonWidth * 2) - buttonGap;

            return new UnlockLayoutMetricsPx
            {
                ClientWidth = clientWidth,
                ClientHeight = scale(dip.ClientHeightDip),

                Margin = margin,
                ContentWidth = Math.Max(0, clientWidth - (margin * 2)),
                PromptTop = scale(dip.PromptTopDip),
                PromptHeight = scale(dip.PromptHeightDip),
                SecretTop = scale(dip.SecretTopDip),
                LinkTop = scale(dip.LinkTopDip),
                LinkHeight = scale(dip.LinkHeightDip),

                RecoveryPasswordLinkWidth = scale(dip.RecoveryPasswordLinkWidthDip),
                RecoveryKeyFileLinkLeft = scale(dip.RecoveryKeyFileLinkLeftDip),
                RecoveryKeyFileLinkWidth = scale(dip.RecoveryKeyFileLinkWidthDip),

                RecoveryKeyIdTop = scale(dip.RecoveryKeyIdTopDip),
                RecoveryKeyIdWidth = scale(dip.RecoveryKeyIdWidthDip),
                RecoveryKeyIdHeight = scale(dip.RecoveryKeyIdHeightDip),

                ButtonTop = scale(dip.ButtonTopDip),
                ButtonWidth = buttonWidth,
                ButtonHeight = scale(dip.ButtonHeightDip),
                UnlockButtonLeft = unlockButtonLeft,
                CancelButtonLeft = unlockButtonLeft + buttonWidth + buttonGap,

                ChromeFontSize = scaleFontPointToPx(dip.ChromeFontSizePt)
            };
        }
    }
}
