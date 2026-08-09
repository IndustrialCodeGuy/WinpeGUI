using Shared.Shell.Interop;
using System.Drawing.Drawing2D;
using System.Text;

namespace Shell.Taskbar.UI
{
    internal enum ShellDesktopWallpaperLayout
    {
        Fit,
        Fill
    }

    internal sealed class ShellDesktopForm : Form
    {
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private static readonly Color DesktopBackColor = Color.Black;
        private Image? _wallpaper;
        private ShellDesktopWallpaperLayout _wallpaperLayout = ShellDesktopWallpaperLayout.Fit;
        private string? _wallpaperPath;

        public ShellDesktopForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            AutoScaleDimensions = new SizeF(96f, 96f);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = string.Empty;
            ControlBox = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = DesktopBackColor;
            Bounds = SystemInformation.VirtualScreen;
            DoubleBuffered = true;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            LoadWallpaper();
        }

        public event EventHandler? DesktopMouseDown;

        public ShellDesktopWallpaperLayout WallpaperLayout
        {
            get => _wallpaperLayout;
            set
            {
                if (_wallpaperLayout == value)
                    return;

                _wallpaperLayout = value;
                Invalidate();
            }
        }


        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE;
                cp.ExStyle &= ~User32.WS_EX_APPWINDOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        public void RefreshDesktopSurface()
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            if (Bounds != bounds)
                Bounds = bounds;

            string? wallpaperPath = ResolveWallpaperPath();
            if (!string.Equals(_wallpaperPath, wallpaperPath, StringComparison.OrdinalIgnoreCase))
                LoadWallpaper(wallpaperPath);

            SendBehindWindows();
            Invalidate();
        }

        public void SendBehindWindows()
        {
            if (!IsHandleCreated || IsDisposed)
                return;

            try
            {
                User32.SetWindowPos(
                    Handle,
                    User32.HWND_BOTTOM,
                    0,
                    0,
                    0,
                    0,
                    User32.SWP_NOMOVE |
                    User32.SWP_NOSIZE |
                    User32.SWP_NOACTIVATE |
                    User32.SWP_SHOWWINDOW);
            }
            catch
            {
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SendBehindWindows();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            DesktopMouseDown?.Invoke(this, EventArgs.Empty);
            SendBehindWindows();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (_wallpaper is null)
            {
                using SolidBrush brush = new(BackColor);
                e.Graphics.FillRectangle(brush, ClientRectangle);
                return;
            }

            e.Graphics.Clear(BackColor);

            Rectangle desktopBounds = Bounds;
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle target = new(
                    screen.Bounds.Left - desktopBounds.Left,
                    screen.Bounds.Top - desktopBounds.Top,
                    screen.Bounds.Width,
                    screen.Bounds.Height);

                DrawWallpaper(e.Graphics, _wallpaper, target, WallpaperLayout);
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_DISPLAYCHANGE = 0x007E;
            const int WM_SETTINGCHANGE = 0x001A;

            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }


            base.WndProc(ref m);

            if (m.Msg != WM_DISPLAYCHANGE && m.Msg != WM_SETTINGCHANGE)
                return;

            if (IsDisposed || Disposing || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(RefreshDesktopSurface));
            }
            catch
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _wallpaper?.Dispose(); } catch { }
                _wallpaper = null;
            }

            base.Dispose(disposing);
        }

        private void LoadWallpaper(string? wallpaperPath = null)
        {
            wallpaperPath ??= ResolveWallpaperPath();

            Image? next = TryLoadWallpaper(wallpaperPath);

            Image? old = _wallpaper;
            _wallpaper = next;
            _wallpaperPath = next is null ? null : wallpaperPath;

            try { old?.Dispose(); } catch { }
        }

        private static Image? TryLoadWallpaper(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                using FileStream stream = File.OpenRead(path);
                using Image source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }

        private static string? ResolveWallpaperPath()
        {
            string? configured = TryGetConfiguredWallpaperPath();
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;

            string windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windowsPath))
            {
                string winPeWallpaper = Path.Combine(windowsPath, "System32", "winpe.jpg");
                if (File.Exists(winPeWallpaper))
                    return winPeWallpaper;

                string windowsWallpaper = Path.Combine(windowsPath, "Web", "Wallpaper", "Windows", "img0.jpg");
                if (File.Exists(windowsWallpaper))
                    return windowsWallpaper;
            }

            return null;
        }

        private static string? TryGetConfiguredWallpaperPath()
        {
            try
            {
                StringBuilder sb = new(1024);
                if (!User32.SystemParametersInfo(
                    User32.SPI_GETDESKWALLPAPER,
                    (uint)sb.Capacity,
                    sb,
                    0))
                {
                    return null;
                }

                string rawPath = sb.ToString();
                int nullIndex = rawPath.IndexOf('\0');
                if (nullIndex >= 0)
                    rawPath = rawPath[..nullIndex];

                string path = Environment.ExpandEnvironmentVariables(rawPath.Trim());
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            catch
            {
                return null;
            }
        }

        private static void DrawWallpaper(Graphics g, Image wallpaper, Rectangle target, ShellDesktopWallpaperLayout layout)
        {
            if (target.Width <= 0 || target.Height <= 0)
                return;

            if (wallpaper.Width == target.Width && wallpaper.Height == target.Height)
            {
                g.DrawImageUnscaled(wallpaper, target.Location);
                return;
            }

            GraphicsState state = g.Save();
            try
            {
                g.SetClip(target);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.None;

                float scale = layout == ShellDesktopWallpaperLayout.Fill
                    ? Math.Max(target.Width / (float)wallpaper.Width, target.Height / (float)wallpaper.Height)
                    : Math.Min(target.Width / (float)wallpaper.Width, target.Height / (float)wallpaper.Height);

                float width = wallpaper.Width * scale;
                float height = wallpaper.Height * scale;
                float left = target.Left + ((target.Width - width) / 2f);
                float top = target.Top + ((target.Height - height) / 2f);

                g.DrawImage(wallpaper, left, top, width, height);
            }
            finally
            {
                g.Restore(state);
            }
        }
    }
}
