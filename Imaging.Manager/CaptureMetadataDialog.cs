using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class CaptureMetadataDialog : Form
{
    private readonly TextBox _name;
    private readonly TextBox _description;

    public CaptureMetadataDialog(string defaultName)
    {
        Text = "Capture FFU";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 178);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        Label nameLabel = new() { Left = 14, Top = 16, Width = 100, Height = 22, Text = "Image name:" };
        _name = new TextBox { Left = 116, Top = 14, Width = 326, Text = defaultName };
        Label descriptionLabel = new() { Left = 14, Top = 52, Width = 100, Height = 22, Text = "Description:" };
        _description = new TextBox { Left = 116, Top = 50, Width = 326, Height = 58, Multiline = true };
        Button cancel = new() { Left = 282, Top = 128, Width = 76, Height = 30, Text = "Cancel", DialogResult = DialogResult.Cancel };
        Button ok = new() { Left = 366, Top = 128, Width = 76, Height = 30, Text = "Capture", DialogResult = DialogResult.OK };
        ok.Click += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                MessageBox.Show(this, "An image name is required by DISM.", "Capture FFU", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        };

        Controls.AddRange(new Control[] { nameLabel, _name, descriptionLabel, _description, cancel, ok });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string ImageName => _name.Text.Trim();
    public string Description => _description.Text.Trim();
}
