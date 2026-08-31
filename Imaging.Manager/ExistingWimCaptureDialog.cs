using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class ExistingWimCaptureDialog : Form
{
    public ExistingWimCaptureDialog(string imagePath)
    {
        Text = "Capture WIM";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, 214);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        Label message = new()
        {
            Left = 16,
            Top = 16,
            Width = 468,
            Height = 112,
            Text = "The selected WIM already exists:\n\n" + imagePath +
                   "\n\nAppend preserves the existing images and adds this capture as a new image index." +
                   "\nAppend modifies the WIM in place; make sure the destination has sufficient free space."
        };

        Button cancel = new()
        {
            Left = 194,
            Top = 162,
            Width = 86,
            Height = 32,
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        Button replace = new()
        {
            Left = 288,
            Top = 162,
            Width = 92,
            Height = 32,
            Text = "Replace",
            DialogResult = DialogResult.No
        };
        Button append = new()
        {
            Left = 388,
            Top = 162,
            Width = 96,
            Height = 32,
            Text = "Append",
            DialogResult = DialogResult.Yes
        };

        Controls.AddRange(new Control[] { message, cancel, replace, append });
        AcceptButton = append;
        CancelButton = cancel;
    }
}
