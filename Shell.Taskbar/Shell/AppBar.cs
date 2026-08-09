using System.Runtime.InteropServices;

namespace Shell.Taskbar.Shell
{
    // =====================================================================
    //  APPBAR DOCKING (Taskbar Reservation)
    // =====================================================================
    //
    // What this does:
    // - Registers the ShellTaskbarForm as an AppBar using SHAppBarMessage (like the real taskbar).
    // - Reserves screen space on a chosen edge so other windows won’t cover it.
    // - Re-applies docking on display/settings changes via a unique callback window message.
    //
    // How ShellTaskbarForm uses it:
    // - ShellTaskbarForm.EnsureAppBarDocked() creates AppBar(this, Bottom, _mPx.TaskbarHeight) and calls RegisterAndDock().
    // - ShellTaskbarForm.WndProc forwards messages to _appBar.HandleWndProc(ref m) so AppBar can re-SetPos() when needed.
    // - ShellTaskbarForm.Dispose() calls _appBar.Unregister() to release the reserved screen area.
    //
    // Notes / Debug tips:
    // - SetPos() uses Screen.FromHandle(_form.Handle).Bounds (monitor bounds) then QUERYPOS/SETPOS to negotiate.
    // - Do not also force SPI_SETWORKAREA here. ABM_QUERYPOS/ABM_SETPOS already cooperate
    //   with the Windows taskbar and other AppBars; a second manual work-area reservation
    //   can feed setting-change messages back into docking during DPI changes.
    // - It forces TopMost=true to keep the taskbar above other windows (WinPE-friendly behavior).
    // - If docking looks wrong after DPI/layout changes, verify EnsureAppBarDocked() is called after metrics settle.
    // =====================================================================

    internal enum AppBarEdge : uint
    {
        Left = 0,
        Top = 1,
        Right = 2,
        Bottom = 3
    }

    internal sealed class AppBar
    {
        private readonly Form _form;
        private readonly AppBarEdge _edge;
        private readonly int _thickness;
        private uint _callbackMsgId;

        public AppBar(Form form, AppBarEdge edge, int thickness)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _edge = edge;
            _thickness = Math.Max(20, thickness);
        }

        public void RegisterAndDock()
        {
            _callbackMsgId = RegisterWindowMessage("MiniTaskbar_AppBar_" + Guid.NewGuid().ToString("N"));
            var abd = NewAbd();
            abd.uCallbackMessage = _callbackMsgId;

            SHAppBarMessage(ABM_NEW, ref abd);
            SetPos();
        }

        public void Unregister()
        {
            var abd = NewAbd();
            SHAppBarMessage(ABM_REMOVE, ref abd);
        }

        public bool HandleWndProc(ref Message m)
        {
            if (m.Msg == _callbackMsgId || m.Msg == WM_DISPLAYCHANGE || m.Msg == WM_SETTINGCHANGE)
            {
                SetPos();
                return true;
            }
            return false;

        }

        private void SetPos()
        {
            var screen = Screen.FromHandle(_form.Handle).Bounds;

            RECT rc = new RECT
            {
                left = screen.Left,
                top = screen.Top,
                right = screen.Right,
                bottom = screen.Bottom
            };

            switch (_edge)
            {
                case AppBarEdge.Bottom: rc.top = rc.bottom - _thickness; break;
                case AppBarEdge.Top: rc.bottom = rc.top + _thickness; break;
                case AppBarEdge.Left: rc.right = rc.left + _thickness; break;
                case AppBarEdge.Right: rc.left = rc.right - _thickness; break;
            }

            var abd = NewAbd();
            abd.uEdge = (uint)_edge;
            abd.rc = rc;

            SHAppBarMessage(ABM_QUERYPOS, ref abd);

            switch (_edge)
            {
                case AppBarEdge.Bottom: abd.rc.top = abd.rc.bottom - _thickness; break;
                case AppBarEdge.Top: abd.rc.bottom = abd.rc.top + _thickness; break;
                case AppBarEdge.Left: abd.rc.right = abd.rc.left + _thickness; break;
                case AppBarEdge.Right: abd.rc.left = abd.rc.right - _thickness; break;
            }

            SHAppBarMessage(ABM_SETPOS, ref abd);

            int width = abd.rc.right - abd.rc.left;
            int height = abd.rc.bottom - abd.rc.top;

            var next = new Rectangle(abd.rc.left, abd.rc.top, width, height);
            if (_form.Bounds != next)
                _form.Bounds = next;

            if (!_form.TopMost)
                _form.TopMost = true;
        }

        private APPBARDATA NewAbd()
        {
            return new APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
                hWnd = _form.Handle
            };
        }

        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int WM_SETTINGCHANGE = 0x001A;

        private const uint ABM_NEW = 0x00000000;
        private const uint ABM_REMOVE = 0x00000001;
        private const uint ABM_QUERYPOS = 0x00000002;
        private const uint ABM_SETPOS = 0x00000003;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public uint cbSize;
            public nint hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public nint lParam;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);
    }
}
