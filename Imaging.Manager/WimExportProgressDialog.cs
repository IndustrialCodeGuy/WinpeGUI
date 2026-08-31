using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class WimExportProgressDialog : Form
{
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private readonly Button _cancel;
    private bool _allowClose;

    public WimExportProgressDialog(string sourcePath, string destinationPath, WimImageInfo image)
    {
        Text = "Export WIM";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(540, 216);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        Label title = new()
        {
            Left = 16,
            Top = 14,
            Width = 508,
            Height = 48,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"Exporting {image.DisplayName}"
        };

        Label files = new()
        {
            Left = 16,
            Top = 54,
            Width = 508,
            Height = 34,
            AutoEllipsis = true,
            Text = $"{Path.GetFileName(sourcePath)}  →  {Path.GetFileName(destinationPath)}"
        };

        _progress = new ProgressBar
        {
            Left = 16,
            Top = 92,
            Width = 508,
            Height = 24,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Marquee
        };
        _status = new Label
        {
            Left = 16,
            Top = 126,
            Width = 508,
            Height = 28,
            AutoEllipsis = true,
            Text = "Starting DISM..."
        };
        _cancel = new Button
        {
            Left = 440,
            Top = 168,
            Width = 84,
            Height = 32,
            Text = "Cancel"
        };
        _cancel.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    this,
                    "Cancel the WIM export?",
                    "Cancel Imaging Operation",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                _cancel.Enabled = false;
                _status.Text = "Canceling...";
                CancelRequested?.Invoke(this, EventArgs.Empty);
            }
        };

        Controls.AddRange(new Control[] { title, files, _progress, _status, _cancel });
    }

    public event EventHandler? CancelRequested;

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
