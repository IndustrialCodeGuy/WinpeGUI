using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class WimServicingProgressDialog : Form
{
    private readonly Label _heading;
    private readonly Label _detail;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private bool _allowClose;

    public WimServicingProgressDialog(string titleText, string heading, string detail)
    {
        Text = titleText;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 166);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        _heading = new Label
        {
            Left = 16,
            Top = 14,
            Width = 528,
            Height = 40,
            Font = new Font(Font, FontStyle.Bold),
            AutoEllipsis = true,
            Text = heading
        };

        _detail = new Label
        {
            Left = 16,
            Top = 50,
            Width = 528,
            Height = 24,
            AutoEllipsis = true,
            Text = detail
        };

        _progress = new ProgressBar
        {
            Left = 16,
            Top = 82,
            Width = 528,
            Height = 24,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Marquee
        };
        _status = new Label
        {
            Left = 16,
            Top = 116,
            Width = 528,
            Height = 28,
            AutoEllipsis = true,
            Text = "Starting DISM..."
        };

        Controls.AddRange(new Control[] { _heading, _detail, _progress, _status });
    }

    public void BeginPhase(string heading, string detail, string status = "Starting DISM...")
    {
        if (IsDisposed)
            return;

        _heading.Text = heading;
        _detail.Text = detail;
        _progress.Style = ProgressBarStyle.Marquee;
        _status.Text = status;
    }

    public void UpdateProgress(WimOperationProgress progress)
    {
        if (IsDisposed)
            return;

        if (progress.Percentage.HasValue)
        {
            if (_progress.Style != ProgressBarStyle.Continuous)
                _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = Math.Clamp(progress.Percentage.Value, 0, 100);
        }

        if (!string.IsNullOrWhiteSpace(progress.Message))
            _status.Text = progress.Message;
    }

    public void AllowClose() => _allowClose = true;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }
}
