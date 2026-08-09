using Shared.Shell.Utilities;

namespace Explorer.Host;

internal sealed class OpenWithCommandDialog : Form
{
    private readonly TextBox _txtCommand;
    private readonly Func<IWin32Window, string?> _browseProgram;
    public string CommandLine => _txtCommand.Text.Trim();

    public OpenWithCommandDialog(Func<IWin32Window, string?> browseProgram)
    {
        Text = "Open With";
        ShellDialogChrome.ApplyFixedDialogDefaults(this);
        ClientSize = new Size(300, 120);

        _browseProgram = browseProgram;

        Label lblDescription = new()
        {
            Text = "Select a program:",
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        ShellDialogChrome.ApplyHeaderFont(this, lblDescription);

        _txtCommand = new TextBox
        {
            Text = "notepad.exe",
            Dock = DockStyle.Fill
        };

        Button btnBrowse = new()
        {
            Text = "Browse..."
        };
        ShellDialogChrome.ApplyStandardButton(btnBrowse);
        btnBrowse.Click += BtnBrowse_Click;

        Button btnOpen = new()
        {
            Text = "Open",
            DialogResult = DialogResult.None
        };
        ShellDialogChrome.ApplyStandardButton(btnOpen);
        btnOpen.Click += BtnOpen_Click;

        Button btnCancel = new()
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        ShellDialogChrome.ApplyStandardButton(btnCancel);

        AcceptButton = btnOpen;
        CancelButton = btnCancel;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = ShellDialogChrome.CompactContentPadding,
            ColumnCount = 1,
            RowCount = 3
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ShellDialogChrome.HeaderLineHeight + 2));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        TableLayoutPanel buttonRow = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };

        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        btnBrowse.Margin = new Padding(0, ShellDialogChrome.ButtonRowTopMargin, 0, 0);
        btnOpen.Margin = new Padding(0, ShellDialogChrome.ButtonRowTopMargin, ShellDialogChrome.ButtonGap, 0);
        btnCancel.Margin = new Padding(0, ShellDialogChrome.ButtonRowTopMargin, 0, 0);

        FlowLayoutPanel rightButtons = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        rightButtons.Controls.Add(btnOpen);
        rightButtons.Controls.Add(btnCancel);

        buttonRow.Controls.Add(btnBrowse, 0, 0);
        buttonRow.Controls.Add(new Label { Dock = DockStyle.Fill }, 1, 0);
        buttonRow.Controls.Add(rightButtons, 2, 0);

        root.Controls.Add(lblDescription, 0, 0);
        root.Controls.Add(_txtCommand, 0, 1);
        root.Controls.Add(buttonRow, 0, 2);

        Controls.Add(root);
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        string? selectedPath = _browseProgram(this);

        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        _txtCommand.Text = $"\"{selectedPath}\"";
        _txtCommand.Focus();
        _txtCommand.SelectionStart = _txtCommand.Text.Length;
    }

    private void BtnOpen_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtCommand.Text))
        {
            MessageBox.Show(
                this,
                "Enter a command to use for this file.",
                "Open With",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            _txtCommand.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        ShellDialogChrome.CenterOnOwnerScreen(this, Owner ?? Form.ActiveForm);

        _txtCommand.Focus();
        _txtCommand.SelectAll();
    }
}