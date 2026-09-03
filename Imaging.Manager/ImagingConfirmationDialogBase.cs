using Shared.Shell.Theming;
using Shared.Shell.Utilities;

namespace Imaging.Manager;

/// <summary>
/// Shared compact layout, chrome, and controls for imaging confirmation dialogs.
/// </summary>
internal abstract class ImagingConfirmationDialogBase : Form
{
    protected const int ContentLeft = 12;
    protected const int ContentRight = 28;

    private const int HeaderTop = 6;
    private const int BottomMargin = 12;
    private const int DefaultGap = 6;
    private const int ButtonGap = 10;
    private Font? _emphasisFont;
    private int _nextTop = HeaderTop;

    protected ImagingConfirmationDialogBase(string windowTitle, int clientWidth)
    {
        Text = windowTitle;
        ShellDialogChrome.ApplyFixedDialogDefaults(this);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = ShellDialogChrome.DialogFont;
        ClientSize = new Size(clientWidth, 1);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;
    }

    protected int ContentWidth => ClientSize.Width - ContentLeft - ContentRight;

    protected int BodyLineHeight => Math.Max(ShellDialogChrome.BodyLineHeight, Font.Height + 4);

    protected void AddHeader(string text, int gapAfter = DefaultGap)
    {
        Label label = new()
        {
            Left = ContentLeft,
            Top = _nextTop,
            Width = ContentWidth,
            AutoEllipsis = true,
            Text = text
        };
        ShellDialogChrome.ApplyHeaderFont(this, label);
        label.Height = Math.Max(ShellDialogChrome.HeaderLineHeight, label.Font.Height + 4);
        Controls.Add(label);
        _nextTop = label.Bottom + gapAfter;
    }

    protected void AddSingleLine(string text, int gapAfter = DefaultGap)
    {
        Label label = new()
        {
            Left = ContentLeft,
            Top = _nextTop,
            Width = ContentWidth,
            Height = BodyLineHeight,
            AutoEllipsis = true,
            Text = text
        };
        Controls.Add(label);
        _nextTop = label.Bottom + gapAfter;
    }

    protected Label AddTextBlock(string text, int gapAfter = DefaultGap, bool emphasis = false)
    {
        Font font = emphasis ? EmphasisFont : Font;
        int measuredHeight = TextRenderer.MeasureText(
            text,
            font,
            new Size(ContentWidth, 10_000),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

        Label label = new()
        {
            Left = ContentLeft,
            Top = _nextTop,
            Width = ContentWidth,
            Height = Math.Max(BodyLineHeight, measuredHeight),
            AutoSize = false,
            Font = font,
            Text = text
        };
        Controls.Add(label);
        _nextTop = label.Bottom + gapAfter;
        return label;
    }

    protected CheckBox AddCheckBox(string text, bool isChecked = false, int gapAfter = DefaultGap)
    {
        CheckBox checkBox = new()
        {
            Left = ContentLeft,
            Top = _nextTop,
            Width = ContentWidth,
            Height = Math.Max(BodyLineHeight, Font.Height + 6),
            Text = text,
            Checked = isChecked
        };
        Controls.Add(checkBox);
        _nextTop = checkBox.Bottom + gapAfter;
        return checkBox;
    }

    protected void AddControlRow(Control control, int height, int gapAfter = DefaultGap)
    {
        control.Left = ContentLeft;
        control.Top = _nextTop;
        control.Width = ContentWidth;
        control.Height = height;
        Controls.Add(control);
        _nextTop = control.Bottom + gapAfter;
    }

    protected Button CreateButton(
        string text,
        DialogResult dialogResult,
        int width = ShellDialogChrome.ButtonWidth,
        bool enabled = true) =>
        new()
        {
            Width = width,
            Height = ShellDialogChrome.ButtonHeight,
            Text = text,
            DialogResult = dialogResult,
            Enabled = enabled
        };

    protected void FinishLayout(Button[] rightButtons, Button? leftButton = null, int gapBefore = 6)
    {
        int buttonTop = _nextTop + gapBefore;
        int right = ClientSize.Width - ContentRight;

        for (int i = rightButtons.Length - 1; i >= 0; i--)
        {
            Button button = rightButtons[i];
            button.Left = right - button.Width;
            button.Top = buttonTop;
            right = button.Left - ButtonGap;
            Controls.Add(button);
        }

        if (leftButton != null)
        {
            leftButton.Left = ContentLeft;
            leftButton.Top = buttonTop;
            Controls.Add(leftButton);
        }

        ClientSize = new Size(
            ClientSize.Width,
            buttonTop + ShellDialogChrome.ButtonHeight + BottomMargin);
    }

    protected static string FormatBytes(ulong bytes)
    {
        const double kb = 1024d;
        const double mb = kb * 1024d;
        const double gb = mb * 1024d;
        const double tb = gb * 1024d;

        if (bytes >= tb) return $"{bytes / tb:0.##} TB";
        if (bytes >= gb) return $"{bytes / gb:0.##} GB";
        if (bytes >= mb) return $"{bytes / mb:0.##} MB";
        if (bytes >= kb) return $"{bytes / kb:0.##} KB";
        return $"{bytes} B";
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _emphasisFont?.Dispose();
    }

    private Font EmphasisFont =>
        _emphasisFont ??= new Font(Font, FontStyle.Bold);
}
