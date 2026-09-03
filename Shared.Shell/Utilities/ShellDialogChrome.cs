namespace Shared.Shell.Utilities;

public static class ShellDialogChrome
{
    public const int ContentMargin = 12;
    public const int CompactContentMargin = 10;

    public const int ButtonWidth = 80;
    public const int ButtonHeight = 25;
    public const int ButtonGap = 10;
    public const int ButtonRowTopMargin = 6;

    public const int FooterGap = 8;
    public const int BodyLineHeight = 18;
    public const int HeaderLineHeight = 22;

    public const float HeaderFontSizeDelta = 1.5f;

    public static Font DialogFont => SystemFonts.MessageBoxFont;
    public static Padding ContentPadding => new(ContentMargin);
    public static Padding CompactContentPadding => new(CompactContentMargin);

    public static void ApplyFixedDialogDefaults(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        form.Font = DialogFont;
        form.StartPosition = FormStartPosition.Manual;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
    }

    public static void ApplyDialogFont(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        control.Font = DialogFont;
    }

    public static void ApplyStandardButton(Button button, int width = ButtonWidth)
    {
        ArgumentNullException.ThrowIfNull(button);

        button.Width = width;
        button.Height = ButtonHeight;
    }

    public static void ApplyTextSafeButton(Button button, int minimumWidth = ButtonWidth)
    {
        ArgumentNullException.ThrowIfNull(button);

        ApplyStandardButton(button, minimumWidth);

        Size preferred = button.GetPreferredSize(Size.Empty);
        Size text = TextRenderer.MeasureText(button.Text ?? string.Empty, button.Font);

        button.Width = Math.Max(button.Width, Math.Max(preferred.Width, text.Width + 20));
        button.Height = Math.Max(button.Height, Math.Max(preferred.Height, text.Height + 8));
    }

    public static void ApplyHeaderFont(Control lifetimeOwner, params Control[] controls)
    {
        ArgumentNullException.ThrowIfNull(lifetimeOwner);

        if (controls == null || controls.Length == 0)
            return;

        Font baseFont = lifetimeOwner.Font ?? DialogFont;
        Font headerFont = new(
            baseFont.FontFamily,
            baseFont.Size + HeaderFontSizeDelta,
            FontStyle.Regular,
            baseFont.Unit);

        foreach (Control control in controls)
        {
            if (control != null)
                control.Font = headerFont;
        }

        lifetimeOwner.Disposed += (_, _) =>
        {
            try
            {
                headerFont.Dispose();
            }
            catch
            {
            }
        };
    }

    public static void ShowCenteredNonModal(Form form, Form? owner)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (form.IsDisposed || form.Visible)
            return;

        try
        {
            CenterOnOwnerScreen(form, owner);

            if (owner != null && !owner.IsDisposed)
                form.Show(owner);
            else
                form.Show();

            form.BringToFront();
            form.Activate();
        }
        catch
        {
            CenterOnOwnerScreen(form, null);
            form.Show();
            form.BringToFront();
            form.Activate();
        }
    }

    public static void ShowCenteredNonModalUnowned(Form form, Form? referenceOwner)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (form.IsDisposed || form.Visible)
            return;

        try
        {
            CenterOnOwnerScreen(form, referenceOwner);
            form.Show();
            form.BringToFront();
            form.Activate();
        }
        catch
        {
            CenterOnOwnerScreen(form, null);
            form.Show();
            form.BringToFront();
            form.Activate();
        }
    }

    public static void CenterOnOwnerScreen(Form form, IWin32Window? owner)
    {
        ArgumentNullException.ThrowIfNull(form);

        Screen screen = Screen.FromPoint(Cursor.Position);

        try
        {
            if (owner is Control ownerControl &&
                !ownerControl.IsDisposed &&
                ownerControl.IsHandleCreated)
            {
                screen = Screen.FromControl(ownerControl);
            }
            else if (owner != null && owner.Handle != IntPtr.Zero)
            {
                screen = Screen.FromHandle(owner.Handle);
            }
        }
        catch
        {
            screen = Screen.FromPoint(Cursor.Position);
        }

        Rectangle area = screen.WorkingArea;

        form.Location = new Point(
            area.Left + ((area.Width - form.Width) / 2),
            area.Top + ((area.Height - form.Height) / 2));
    }

    public static Form? GetDialogOwner()
    {
        try
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form.ContainsFocus)
                    return form;
            }

            return Form.ActiveForm;
        }
        catch
        {
            return Form.ActiveForm;
        }
    }

    public static Form? GetRestoreOwner(Form dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        try
        {
            Form? owner = dialog.Owner;

            if (owner == null ||
                owner.IsDisposed ||
                !owner.Visible ||
                !dialog.Visible ||
                (!dialog.ContainsFocus && !ReferenceEquals(Form.ActiveForm, dialog)))
            {
                return null;
            }

            return owner;
        }
        catch
        {
            return null;
        }
    }

    public static void SafeCloseAndDispose(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        try
        {
            if (!form.IsDisposed)
                form.Close();
        }
        catch
        {
        }

        try
        {
            if (!form.IsDisposed)
                form.Dispose();
        }
        catch
        {
        }
    }

    public static void RestoreOwner(Form? owner)
    {
        if (owner == null || owner.IsDisposed || !owner.Visible)
            return;

        try
        {
            owner.BeginInvoke(new Action(() =>
            {
                if (owner.IsDisposed || !owner.Visible)
                    return;

                if (owner.WindowState == FormWindowState.Minimized)
                    owner.WindowState = FormWindowState.Normal;

                owner.Activate();
            }));
        }
        catch
        {
        }
    }

    public static void ShowError(Form? owner, string message, string caption)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (owner != null && !owner.IsDisposed)
        {
            MessageBox.Show(
                owner,
                message,
                caption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        else
        {
            MessageBox.Show(
                message,
                caption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}