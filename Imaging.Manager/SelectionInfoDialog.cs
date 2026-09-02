using Shared.Shell.Theming;

namespace Imaging.Manager;

internal sealed class SelectionInfoDialog : Form
{
    public SelectionInfoDialog(string title, string details)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 540);
        MinimumSize = new Size(520, 360);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        TextBox text = new()
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.DarkMode ? ShellTheme.TextColor : Color.Black,
            Font = new Font("Consolas", 9f),
            Text = details ?? string.Empty,
            TabStop = false
        };

        Button close = new()
        {
            Text = "Close",
            Width = 88,
            Height = 32,
            DialogResult = DialogResult.OK
        };

        Controls.Add(text);
        Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;

        void layout()
        {
            int margin = 12;
            int gap = 10;
            int buttonBottom = ClientSize.Height - margin;
            close.Left = ClientSize.Width - margin - close.Width;
            close.Top = buttonBottom - close.Height;
            text.SetBounds(
                margin,
                margin,
                Math.Max(1, ClientSize.Width - (margin * 2)),
                Math.Max(1, close.Top - gap - margin));
        }

        Resize += (_, _) => layout();
        layout();
    }
}
