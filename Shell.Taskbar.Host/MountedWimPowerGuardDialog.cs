using Shared.Shell.Theming;

namespace Shell.Taskbar.Host;

internal enum MountedWimPowerChoice
{
    Cancel,
    OpenImagingManager,
    ContinueAnyway
}

internal sealed class MountedWimPowerGuardDialog : Form
{
    private readonly Button _openButton;
    private MountedWimPowerChoice _choice = MountedWimPowerChoice.Cancel;

    public MountedWimPowerGuardDialog(
        IReadOnlyList<MountedWimPowerImage> images,
        bool reboot,
        bool imagingManagerAvailable)
    {
        ArgumentNullException.ThrowIfNull(images);

        Text = reboot ? "Restart - Mounted WIMs" : "Shutdown - Mounted WIMs";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 320);
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;
        Font = SystemFonts.MessageBoxFont;

        Label header = new()
        {
            AutoSize = false,
            Text = $"{images.Count} mounted WIM{(images.Count == 1 ? " is" : "s are")} still registered.",
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(16, 16),
            Size = new Size(588, 22),
            ForeColor = ShellTheme.TextColor
        };

        Label message = new()
        {
            AutoSize = false,
            Text = "Commit or discard writable mounts, or recover unhealthy mounts, before powering off. " +
                   "Continuing anyway can lose uncommitted WIM changes. In WinPE the mount registration will not survive the reboot/shutdown.",
            Location = new Point(16, 44),
            Size = new Size(588, 54),
            ForeColor = ShellTheme.TextColor
        };

        ListBox list = new()
        {
            Location = new Point(16, 106),
            Size = new Size(588, 132),
            IntegralHeight = false,
            HorizontalScrollbar = true,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.TextColor,
            BorderStyle = BorderStyle.FixedSingle
        };
        foreach (MountedWimPowerImage image in images)
            list.Items.Add(image.DisplayText);

        _openButton = new Button
        {
            Text = "Open Imaging Manager",
            Size = new Size(152, 30),
            Location = new Point(16, 270),
            Enabled = imagingManagerAvailable,
            BackColor = ShellTheme.ButtonDefault,
            ForeColor = ShellTheme.TextColor,
            UseVisualStyleBackColor = false
        };
        _openButton.Click += (_, _) => CloseWithChoice(MountedWimPowerChoice.OpenImagingManager);

        Button continueButton = new()
        {
            Text = reboot ? "Restart Anyway" : "Shutdown Anyway",
            Size = new Size(136, 30),
            Location = new Point(316, 270),
            BackColor = ShellTheme.ButtonDefault,
            ForeColor = ShellTheme.TextColor,
            UseVisualStyleBackColor = false
        };
        continueButton.Click += (_, _) => CloseWithChoice(MountedWimPowerChoice.ContinueAnyway);

        Button cancelButton = new()
        {
            Text = "Cancel",
            Size = new Size(136, 30),
            Location = new Point(468, 270),
            DialogResult = DialogResult.Cancel,
            BackColor = ShellTheme.ButtonDefault,
            ForeColor = ShellTheme.TextColor,
            UseVisualStyleBackColor = false
        };
        cancelButton.Click += (_, _) => CloseWithChoice(MountedWimPowerChoice.Cancel);

        Controls.Add(header);
        Controls.Add(message);
        Controls.Add(list);
        Controls.Add(_openButton);
        Controls.Add(continueButton);
        Controls.Add(cancelButton);

        CancelButton = cancelButton;
        AcceptButton = imagingManagerAvailable ? _openButton : cancelButton;
    }

    public MountedWimPowerChoice ShowGuardDialog()
    {
        _choice = MountedWimPowerChoice.Cancel;
        ShowDialog();
        return _choice;
    }

    public MountedWimPowerChoice ShowGuardDialog(IWin32Window owner)
    {
        _choice = MountedWimPowerChoice.Cancel;
        ShowDialog(owner);
        return _choice;
    }

    private void CloseWithChoice(MountedWimPowerChoice choice)
    {
        _choice = choice;
        DialogResult = choice == MountedWimPowerChoice.Cancel ? DialogResult.Cancel : DialogResult.OK;
        Close();
    }
}
