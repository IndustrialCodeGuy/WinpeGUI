using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class WimCaptureProgressDialog : Form
{
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private readonly Button _cancel;
    private bool _allowClose;

    public WimCaptureProgressDialog(ImagingPartitionInfo partition, string sourceRoot, string imagePath)
    {
        Text = "Capture WIM";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 190);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        string sourceName = sourceRoot.TrimEnd('\\');
        if (sourceName.Length == 0)
            sourceName = $"Partition {partition.PartitionNumber}";

        Label title = new()
        {
            Left = 16,
            Top = 14,
            Width = 488,
            Height = 48,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"Capturing {sourceName} to {Path.GetFileName(imagePath)}"
        };

        _progress = new ProgressBar
        {
            Left = 16,
            Top = 70,
            Width = 488,
            Height = 24,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Marquee
        };
        _status = new Label
        {
            Left = 16,
            Top = 104,
            Width = 488,
            Height = 28,
            AutoEllipsis = true,
            Text = "Starting DISM..."
        };
        _cancel = new Button
        {
            Left = 420,
            Top = 142,
            Width = 84,
            Height = 32,
            Text = "Cancel"
        };
        _cancel.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    this,
                    "Cancel the WIM capture?",
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

        Controls.AddRange(new Control[] { title, _progress, _status, _cancel });
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
