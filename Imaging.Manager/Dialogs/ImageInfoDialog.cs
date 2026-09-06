using Shared.Shell.Theming;
using Shared.Shell.Utilities;

namespace Imaging.Manager;

internal sealed class ImageInfoDialog : Form
{
    private const int ClientWidth = 760;
    private const int ClientHeight = 540;
    private const int MarginSize = 12;
    private const int Gap = 8;

    private readonly TextBox _details;
    private readonly Button _closeButton;

    public ImageInfoDialog(string imagePath, string output)
    {
        Text = "Image Info";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = ShellDialogChrome.DialogFont;
        ClientSize = new Size(ClientWidth, ClientHeight);
        MinimumSize = new Size(560, 380);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        Label fileLabel = new()
        {
            AutoSize = false,
            AutoEllipsis = true,
            Text = $"Image File: {imagePath}"
        };

        _details = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.DarkMode ? ShellTheme.TextColor : Color.Black,
            Font = new Font("Consolas", 9f),
            Text = output ?? string.Empty,
            TabStop = false
        };

        _closeButton = new Button { Text = "Close", DialogResult = DialogResult.Cancel };
        ShellDialogChrome.ApplyTextSafeButton(_closeButton, 88);

        Controls.Add(fileLabel);
        Controls.Add(_details);
        Controls.Add(_closeButton);

        CancelButton = _closeButton;

        void layout()
        {
            int bodyHeight = Math.Max(ShellDialogChrome.BodyLineHeight, Font.Height + 4);
            fileLabel.SetBounds(MarginSize, MarginSize, Math.Max(1, ClientSize.Width - (MarginSize * 2)), bodyHeight);

            int top = fileLabel.Bottom + Gap;
            int buttonTop = ClientSize.Height - MarginSize - _closeButton.Height;
            int detailsBottom = buttonTop - Gap;
            _details.SetBounds(
                MarginSize,
                top,
                Math.Max(1, ClientSize.Width - (MarginSize * 2)),
                Math.Max(1, detailsBottom - top));

            _closeButton.Left = ClientSize.Width - MarginSize - _closeButton.Width;
            _closeButton.Top = buttonTop;
        }

        Resize += (_, _) => layout();
        layout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _details.Font.Dispose();
        base.Dispose(disposing);
    }
}
