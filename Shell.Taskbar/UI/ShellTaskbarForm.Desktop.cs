using Shared.Shell.Interop;

namespace Shell.Taskbar.UI
{
    public sealed partial class ShellTaskbarForm : Form
    {
        // WinPE desktop click surface. It stays behind app windows, mirrors the
        // configured wallpaper, and gives empty-desktop clicks somewhere to land.

        #region Desktop Surface

        private const ShellDesktopWallpaperLayout DesktopWallpaperLayout = ShellDesktopWallpaperLayout.Fill;
        private ShellDesktopForm? _desktop;

        private void EnsureDesktopSurface()
        {
            if (!_isWinPE)
                return;

            if (_desktop is { IsDisposed: false })
            {
                _desktop.RefreshDesktopSurface();
                return;
            }

            ShellDesktopForm desktop = new()
            {
                WallpaperLayout = DesktopWallpaperLayout
            };

            desktop.DesktopMouseDown += Desktop_DesktopMouseDown;
            desktop.FormClosed += Desktop_FormClosed;

            _desktop = desktop;
            desktop.Show();
            desktop.RefreshDesktopSurface();
        }

        private void Desktop_DesktopMouseDown(object? sender, EventArgs e)
        {
            CloseStartSurfaces();
            ClearFocusedAppState();
            ActiveControl = null;
            TryActivateTaskbarForDesktopClick();
        }

        private void TryActivateTaskbarForDesktopClick()
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
                return;

            try
            {
                User32.SetForegroundWindow(Handle);
            }
            catch
            {
            }
        }

        private void Desktop_FormClosed(object? sender, FormClosedEventArgs e)
        {
            if (sender is ShellDesktopForm desktop)
                DetachDesktop(desktop);

            if (ReferenceEquals(_desktop, sender))
                _desktop = null;
        }

        private void DisposeDesktopSurface()
        {
            ShellDesktopForm? desktop = _desktop;
            if (desktop is null)
                return;

            _desktop = null;
            DetachDesktop(desktop);

            try
            {
                if (!desktop.IsDisposed)
                    desktop.Dispose();
            }
            catch
            {
            }
        }

        private void DetachDesktop(ShellDesktopForm desktop)
        {
            desktop.DesktopMouseDown -= Desktop_DesktopMouseDown;
            desktop.FormClosed -= Desktop_FormClosed;
        }

        #endregion
    }
}
