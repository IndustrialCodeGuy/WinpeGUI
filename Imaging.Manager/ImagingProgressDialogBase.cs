using Shared.Shell.Theming;
using Shared.Shell.Utilities;

namespace Imaging.Manager;

/// <summary>
/// Shared presentation and behavior for imaging operations that report DISM-style progress.
/// </summary>
internal class ImagingProgressDialogBase : Form
{
    private const int ClientWidth = 540;
    private const int ContentLeft = 12;
    private const int ContentRight = 28;
    private const int ContentWidth = ClientWidth - ContentLeft - ContentRight;
    private const int HeaderTop = 6;
    private const int ProgressHeight = 22;
    private const int BottomMargin = 12;

    private readonly Label _heading;
    private readonly Label _detail;
    private readonly Label? _secondaryDetail;
    private readonly Label _status;
    private readonly ProgressBar _progress;
    private readonly Button? _cancelButton;
    private readonly string? _cancelConfirmation;
    private readonly string _cancelDialogTitle;
    private bool _allowClose;

    protected ImagingProgressDialogBase(
        string windowTitle,
        string heading,
        string initialStatus,
        string detail,
        string? secondaryDetail = null,
        string? cancelConfirmation = null,
        string cancelDialogTitle = "Cancel Imaging Operation")
    {
        Text = windowTitle;
        ShellDialogChrome.ApplyFixedDialogDefaults(this);
        StartPosition = FormStartPosition.CenterParent;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = ShellDialogChrome.DialogFont;
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        _heading = new Label
        {
            Left = ContentLeft,
            Top = HeaderTop,
            Width = ContentWidth,
            AutoEllipsis = true,
            Text = heading
        };
        ShellDialogChrome.ApplyHeaderFont(this, _heading);

        // Match the compact file-operation rhythm, while allowing WinPE font
        // metrics enough room to avoid clipping either text row.
        int headerHeight = Math.Max(ShellDialogChrome.HeaderLineHeight, _heading.Font.Height + 4);
        int bodyHeight = Math.Max(ShellDialogChrome.BodyLineHeight, Font.Height + 4);
        int statusTop = HeaderTop + headerHeight + 6;
        int progressTop = statusTop + bodyHeight;
        int detailTop = progressTop + ProgressHeight + 8;
        int contentBottom = detailTop + bodyHeight;
        if (secondaryDetail != null)
            contentBottom += bodyHeight;
        int buttonTop = contentBottom + 12;

        _heading.Height = headerHeight;

        _detail = new Label
        {
            Left = ContentLeft,
            Top = detailTop,
            Width = ContentWidth,
            Height = bodyHeight,
            AutoEllipsis = true,
            Text = detail
        };

        _status = new Label
        {
            Left = ContentLeft,
            Top = statusTop,
            Width = ContentWidth,
            Height = bodyHeight,
            AutoEllipsis = true,
            Text = initialStatus
        };

        _progress = new ProgressBar
        {
            Left = ContentLeft,
            Top = progressTop,
            Width = ContentWidth,
            Height = ProgressHeight,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Marquee
        };

        Controls.AddRange(new Control[] { _heading, _status, _progress, _detail });

        if (secondaryDetail != null)
        {
            _secondaryDetail = new Label
            {
                Left = ContentLeft,
                Top = detailTop + bodyHeight,
                Width = ContentWidth,
                Height = bodyHeight,
                AutoEllipsis = true,
                Text = secondaryDetail
            };
            Controls.Add(_secondaryDetail);
        }

        _cancelConfirmation = cancelConfirmation;
        _cancelDialogTitle = cancelDialogTitle;
        ClientSize = new Size(
            ClientWidth,
            cancelConfirmation == null
                ? contentBottom + BottomMargin
                : buttonTop + ShellDialogChrome.ButtonHeight + BottomMargin);

        if (cancelConfirmation != null)
        {
            _cancelButton = new Button
            {
                Left = ClientWidth - ContentRight - ShellDialogChrome.ButtonWidth,
                Top = buttonTop,
                Text = "Cancel"
            };
            ShellDialogChrome.ApplyStandardButton(_cancelButton);
            _cancelButton.Click += CancelButton_Click;
            CancelButton = _cancelButton;
            Controls.Add(_cancelButton);
        }
    }

    public event EventHandler? CancelRequested;

    public void AllowClose() => _allowClose = true;

    protected void ApplyProgressUpdate(int? percentage, string? message, bool restoreMarquee = false)
    {
        if (IsDisposed)
            return;

        if (percentage.HasValue)
        {
            if (_progress.Style != ProgressBarStyle.Continuous)
                _progress.Style = ProgressBarStyle.Continuous;

            _progress.Value = Math.Clamp(percentage.Value, 0, 100);
        }
        else if (restoreMarquee && _progress.Value == 0 && _progress.Style != ProgressBarStyle.Marquee)
        {
            _progress.Style = ProgressBarStyle.Marquee;
        }

        if (!string.IsNullOrWhiteSpace(message))
            _status.Text = message;
    }

    protected void SetIndeterminateStatus(string status, bool disableCancel = false)
    {
        if (IsDisposed)
            return;

        if (disableCancel && _cancelButton != null)
            _cancelButton.Enabled = false;

        _progress.Style = ProgressBarStyle.Marquee;
        _status.Text = status;
    }

    protected void SetPhase(string heading, string detail, string status)
    {
        if (IsDisposed)
            return;

        _heading.Text = heading;
        _detail.Text = detail;
        _progress.Style = ProgressBarStyle.Marquee;
        _status.Text = status;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        if (_cancelButton == null || _cancelConfirmation == null)
            return;

        if (MessageBox.Show(
                this,
                _cancelConfirmation,
                _cancelDialogTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _cancelButton.Enabled = false;
        _status.Text = "Canceling...";
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
