using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class WimApplyProgressDialog : Form
{
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private readonly Button _cancel;
    private bool _allowClose;

    public WimApplyProgressDialog(
        ImagingPartitionInfo partition,
        string targetRoot,
        string imagePath,
        WimImageInfo image)
    {
        Text = "Apply WIM";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 204);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        string targetAccess = targetRoot.TrimEnd('\\');
        string targetName = partition.DriveLetters.Count > 0 && targetAccess.Length > 0
            ? targetAccess
            : $"Partition {partition.PartitionNumber}";

        string imageName = string.IsNullOrWhiteSpace(image.Name)
            ? $"Index {image.Index}"
            : image.Name;

        Label title = new()
        {
            Left = 16,
            Top = 14,
            Width = 488,
            Height = 58,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"Applying {imageName} to {targetName}\n{Path.GetFileName(imagePath)}"
        };

        _progress = new ProgressBar
        {
            Left = 16,
            Top = 78,
            Width = 488,
            Height = 24,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Marquee
        };
        _status = new Label
        {
            Left = 16,
            Top = 112,
            Width = 488,
            Height = 28,
            AutoEllipsis = true,
            Text = "Starting DISM..."
        };
        _cancel = new Button
        {
            Left = 420,
            Top = 156,
            Width = 84,
            Height = 32,
            Text = "Cancel"
        };
        _cancel.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    this,
                    "Cancel the WIM apply operation?\n\nThe target partition may be left with a partially applied image.",
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
