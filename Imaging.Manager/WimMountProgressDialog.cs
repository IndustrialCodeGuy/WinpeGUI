using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class WimMountProgressDialog : Form
{
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private bool _allowClose;

    public WimMountProgressDialog(string imagePath, string mountDirectory, WimImageInfo image)
    {
        Text = "Mount WIM";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(540, 166);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        Label title = new()
        {
            Left = 16,
            Top = 14,
            Width = 508,
            Height = 40,
            Font = new Font(Font, FontStyle.Bold),
            AutoEllipsis = true,
            Text = $"Mounting {image.DisplayName} from {Path.GetFileName(imagePath)}"
        };

        Label folder = new()
        {
            Left = 16,
            Top = 50,
            Width = 508,
            Height = 24,
            AutoEllipsis = true,
            Text = mountDirectory
        };

        _progress = new ProgressBar
        {
            Left = 16,
            Top = 82,
            Width = 508,
            Height = 24,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Marquee
        };
        _status = new Label
        {
            Left = 16,
            Top = 116,
            Width = 508,
            Height = 28,
            AutoEllipsis = true,
            Text = "Starting DISM..."
        };

        Controls.AddRange(new Control[] { title, folder, _progress, _status });
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
