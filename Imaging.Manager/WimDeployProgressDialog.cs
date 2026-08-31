using Imaging.Core;
using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class WimDeployProgressDialog : Form
{
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private readonly Button _cancel;
    private bool _allowClose;

    public WimDeployProgressDialog(ImagingDiskInfo disk, string imagePath, WimImageInfo image)
    {
        Text = "Deploy WIM";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(540, 224);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        string imageName = string.IsNullOrWhiteSpace(image.Name)
            ? $"Index {image.Index}"
            : image.Name;

        Label title = new()
        {
            Left = 16,
            Top = 14,
            Width = 508,
            Height = 62,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"Deploying {imageName} to Disk {disk.DiskNumber}\n{Path.GetFileName(imagePath)}"
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
            Height = 50,
            AutoEllipsis = true,
            Text = "Preparing deployment..."
        };

        _cancel = new Button
        {
            Left = 440,
            Top = 176,
            Width = 84,
            Height = 32,
            Text = "Cancel"
        };
        _cancel.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    this,
                    "Cancel the WIM deployment?\n\nThe target disk may already have been erased and can be left unbootable or partially deployed.",
                    "Cancel Deployment",
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

    public void UpdateProgress(WimDeploymentProgress progress)
    {
        if (IsDisposed)
            return;

        if (progress.Percentage.HasValue)
        {
            if (_progress.Style != ProgressBarStyle.Continuous)
                _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = Math.Clamp(progress.Percentage.Value, 0, 100);
        }
        else if (_progress.Value == 0 && _progress.Style != ProgressBarStyle.Marquee)
        {
            _progress.Style = ProgressBarStyle.Marquee;
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
