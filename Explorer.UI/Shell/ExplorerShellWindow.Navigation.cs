using Shell.Core.Models;

namespace Explorer.UI.Shell;

public partial class ExplorerShellWindow
{
    internal void SetAddressText(string text)
    {
        text ??= string.Empty;
        _currentAddressText = text;

        if (_isAddressTextMode)
            return;

        if(!string.Equals(_txtPath.Text, text, StringComparison.Ordinal))
            _txtPath.Text = text;

        RenderAddressLinks();
    }

    internal void SetWindowTitle(string text)
    {
        if (_mode != ExplorerWindowMode.Browse || string.IsNullOrWhiteSpace(text))
            return;

        if (!string.Equals(Text, text, StringComparison.Ordinal))
            Text = text;
    }

    internal void SetNavigationButtonState(bool canBack, bool canForward, bool canUp)
    {
        SetToolbarGlyphButtonEnabled(_btnBack, canBack);
        SetToolbarGlyphButtonEnabled(_btnForward, canForward);
        SetToolbarGlyphButtonEnabled(_btnUp, canUp);
    }

    internal void SetStatusText(string text)
    {
        text ??= string.Empty;

        if (!string.Equals(_lblStatus.Text, text, StringComparison.Ordinal))
            _lblStatus.Text = text;
    }

    private void HookExtendedMouseButtons(Control control)
    {
        control.MouseUp += ExtendedMouseButtons_MouseUp;
        control.ControlAdded += ExtendedMouseButtons_ControlAdded;

        foreach (Control child in control.Controls)
            HookExtendedMouseButtons(child);
    }

    private void ExtendedMouseButtons_ControlAdded(object? sender, ControlEventArgs e)
    {
        HookExtendedMouseButtons(e.Control);
    }

    private void ExtendedMouseButtons_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.XButton1)
        {
            if (IsToolbarGlyphButtonEnabled(_btnBack))
                _presenter.NavigateBack();

            return;
        }

        if (e.Button == MouseButtons.XButton2)
        {
            if (IsToolbarGlyphButtonEnabled(_btnForward))
                _presenter.NavigateForward();

            return;
        }
    }
}