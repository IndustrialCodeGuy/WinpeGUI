using Shared.Shell.Interop;
using Shell.Core.Models;
using System.Runtime.InteropServices;

namespace Explorer.UI.Shell;

public partial class ExplorerShellWindow
{
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        DpiWindowResizeState? resizeState = CreateDpiWindowResizeState(e);

        // Child controls receive WM_DPICHANGED_BEFOREPARENT before the form receives
        // OnDpiChanged. The TreeView hook freezes redraw and clears the heavily-loaded
        // tree before native child-DPI handling reaches WM_SETFONT. Keep this guard
        // because the form-level path can still be the first path hit in some cases.
        BeginDpiRedrawFreeze();
        PrepareTreeForDpiChangeOnce();

        try
        {
            base.OnDpiChanged(e);
            ReapplyDpiMetrics(refreshContent: true, resizeState);
        }
        finally
        {
            _treePreparedForDpiChange = false;
            EndDpiRedrawFreeze();
        }
    }

    private void InstallTreeDpiPrepareHook()
    {
        if (_treeDpiPrepareHook is not null || _tvNav.IsDisposed)
            return;

        _treeDpiPrepareHook = new TreeDpiPrepareHook(
            _tvNav,
            PrepareTreeForDpiChangeBeforeChildDpi
        );
    }

    private void PrepareTreeForDpiChangeBeforeChildDpi()
    {
        if (IsDisposed || _windowResourcesReleased)
            return;

        BeginDpiRedrawFreeze();
        PrepareTreeForDpiChangeOnce();
    }

    private void PrepareTreeForDpiChangeOnce()
    {
        if (_treePreparedForDpiChange)
            return;

        _treePreparedForDpiChange = true;
        _presenter.PrepareTreeForDpiChange();
    }

    private void BeginDpiRedrawFreeze()
    {
        if (_dpiRedrawFreezeActive || IsDisposed || !IsHandleCreated)
            return;

        _dpiRedrawFreezeActive = true;
        _dpiRedrawFrozenHandles.Clear();

        CollectControlHandles(this, _dpiRedrawFrozenHandles);

        foreach (IntPtr handle in _dpiRedrawFrozenHandles)
            User32.SendMessage(handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
    }

    private void EndDpiRedrawFreeze()
    {
        if (!_dpiRedrawFreezeActive)
            return;

        for (int i = _dpiRedrawFrozenHandles.Count - 1; i >= 0; i--)
            User32.SendMessage(_dpiRedrawFrozenHandles[i], WmSetRedraw, new IntPtr(1), IntPtr.Zero);

        _dpiRedrawFrozenHandles.Clear();
        _dpiRedrawFreezeActive = false;

        if (!IsDisposed && IsHandleCreated)
        {
            RedrawWindow(
                Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                RedrawWindowFlags.Invalidate |
                RedrawWindowFlags.Erase |
                RedrawWindowFlags.Frame |
                RedrawWindowFlags.AllChildren |
                RedrawWindowFlags.UpdateNow);
        }
    }

    private static void CollectControlHandles(Control control, List<IntPtr> handles)
    {
        if (!control.IsDisposed && control.IsHandleCreated)
            handles.Add(control.Handle);

        foreach (Control child in control.Controls)
            CollectControlHandles(child, handles);
    }

    private sealed class TreeDpiPrepareHook : NativeWindow, IDisposable
    {
        private const int WmDpiChangedBeforeParent = 0x02E2;

        private readonly Control _control;
        private readonly Action _prepareTreeForDpiChange;
        private bool _disposed;

        public TreeDpiPrepareHook(
            Control control,
            Action prepareTreeForDpiChange)
        {
            _control = control;
            _prepareTreeForDpiChange = prepareTreeForDpiChange;

            _control.HandleCreated += Control_HandleCreated;
            _control.HandleDestroyed += Control_HandleDestroyed;

            if (_control.IsHandleCreated)
                AssignHandle(_control.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            bool isDpiBeforeParent = m.Msg == WmDpiChangedBeforeParent;

            if (isDpiBeforeParent)
                _prepareTreeForDpiChange();

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _control.HandleCreated -= Control_HandleCreated;
            _control.HandleDestroyed -= Control_HandleDestroyed;

            if (Handle != IntPtr.Zero)
                ReleaseHandle();
        }

        private void Control_HandleCreated(object? sender, EventArgs e)
        {
            if (!_disposed && _control.IsHandleCreated)
                AssignHandle(_control.Handle);
        }

        private void Control_HandleDestroyed(object? sender, EventArgs e)
        {
            if (Handle != IntPtr.Zero)
                ReleaseHandle();
        }
    }

    private const int WmSetRedraw = 0x000B;

    [Flags]
    private enum RedrawWindowFlags : uint
    {
        Invalidate = 0x0001,
        Erase = 0x0004,
        AllChildren = 0x0080,
        UpdateNow = 0x0100,
        Frame = 0x0400
    }

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(
        IntPtr hWnd,
        IntPtr lprcUpdate,
        IntPtr hrgnUpdate,
        RedrawWindowFlags flags);

    private DpiWindowResizeState? CreateDpiWindowResizeState(DpiChangedEventArgs e)
    {
        if (WindowState == FormWindowState.Normal)
        {
            return new DpiWindowResizeState(
                ClientSize,
                e.DeviceDpiOld,
                e.DeviceDpiNew,
                e.SuggestedRectangle);
        }

        if (WindowState != FormWindowState.Minimized)
            return null;

        Size normalClientSize = GetTrackedNormalClientSize();
        Rectangle restoreBounds = RestoreBounds;

        if (normalClientSize.Width <= 0 || normalClientSize.Height <= 0 ||
            restoreBounds.Width <= 0 || restoreBounds.Height <= 0)
        {
            return null;
        }

        return new DpiWindowResizeState(
            normalClientSize,
            e.DeviceDpiOld,
            e.DeviceDpiNew,
            restoreBounds);
    }

    private void TrackNormalClientSize()
    {
        if (WindowState != FormWindowState.Normal ||
            ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        _lastNormalClientSize = ClientSize;
        _minimizedDpiRestoreBounds = null;
    }

    private Size GetTrackedNormalClientSize()
    {
        if (_lastNormalClientSize.Width > 0 && _lastNormalClientSize.Height > 0)
            return _lastNormalClientSize;

        return ClientSize.Width > 0 && ClientSize.Height > 0
            ? ClientSize
            : Size.Empty;
    }

    private readonly struct DpiWindowResizeState
    {
        public DpiWindowResizeState(
            Size oldClientSize,
            int oldDpi,
            int newDpi,
            Rectangle suggestedBounds)
        {
            OldClientSize = oldClientSize;
            OldDpi = oldDpi;
            NewDpi = newDpi;
            SuggestedBounds = suggestedBounds;
        }

        public readonly Size OldClientSize;
        public readonly int OldDpi;
        public readonly int NewDpi;
        public readonly Rectangle SuggestedBounds;
    }

    private void ApplyScaledDpiWindowBounds(DpiWindowResizeState? resizeState)
    {
        if (!resizeState.HasValue)
            return;

        Rectangle targetBounds = GetScaledDpiWindowBounds(resizeState.Value);

        if (WindowState == FormWindowState.Minimized)
        {
            if (TrySetMinimizedRestoreBounds(targetBounds))
                _minimizedDpiRestoreBounds = targetBounds;

            return;
        }

        if (WindowState != FormWindowState.Normal)
            return;

        Bounds = targetBounds;
        TrackNormalClientSize();
    }

    private Rectangle GetScaledDpiWindowBounds(DpiWindowResizeState state)
    {
        int oldDpi = Math.Max(1, state.OldDpi);
        int newDpi = Math.Max(1, state.NewDpi);

        Size targetClientSize = new(
            Math.Max(1, (int)Math.Round(state.OldClientSize.Width * (newDpi / (double)oldDpi))),
            Math.Max(1, (int)Math.Round(state.OldClientSize.Height * (newDpi / (double)oldDpi))));

        Size targetWindowSize = SizeFromClientSize(targetClientSize);
        targetWindowSize.Width = Math.Max(targetWindowSize.Width, MinimumSize.Width);
        targetWindowSize.Height = Math.Max(targetWindowSize.Height, MinimumSize.Height);

        Rectangle targetBounds = new(state.SuggestedBounds.Location, targetWindowSize);

        return FitBoundsToDpiAvailableArea(targetBounds);
    }

    private bool TrySetMinimizedRestoreBounds(Rectangle bounds)
    {
        if (IsDisposed || !IsHandleCreated || WindowState != FormWindowState.Minimized)
            return false;

        User32.WINDOWPLACEMENT placement = new()
        {
            length = Marshal.SizeOf<User32.WINDOWPLACEMENT>()
        };

        if (!User32.GetWindowPlacement(Handle, ref placement))
            return false;

        placement.rcNormalPosition = new User32.RECT
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Right = bounds.Right,
            Bottom = bounds.Bottom
        };

        return User32.SetWindowPlacement(Handle, ref placement);
    }

    private Rectangle FitBoundsToDpiAvailableArea(Rectangle bounds)
    {
        Rectangle availableBounds = GetDpiAvailableBounds(bounds);

        if (availableBounds.Width <= 0 || availableBounds.Height <= 0)
            return bounds;

        int width = Math.Min(bounds.Width, availableBounds.Width);
        int height = Math.Min(bounds.Height, availableBounds.Height);

        int x = Math.Min(
            Math.Max(bounds.X, availableBounds.Left),
            availableBounds.Right - width);

        int y = Math.Min(
            Math.Max(bounds.Y, availableBounds.Top),
            availableBounds.Bottom - height);

        return new Rectangle(x, y, width, height);
    }

    private void ApplyExplorerMinimumSize(Rectangle? preferredBounds = null)
    {
        Size minimumSize = GetExplorerMinimumWindowSize();
        Rectangle availableBounds = GetDpiAvailableBounds(preferredBounds ?? Bounds);

        if (availableBounds.Width > 0)
            minimumSize.Width = Math.Min(minimumSize.Width, availableBounds.Width);

        if (availableBounds.Height > 0)
            minimumSize.Height = Math.Min(minimumSize.Height, availableBounds.Height);

        MinimumSize = minimumSize;
    }

    private Size GetExplorerMinimumWindowSize()
    {
        int minimumClientWidth =
            GetMinimumNavPaneWidth() +
            _splitMain.SplitterWidth +
            GetMinimumListPaneWidth();

        int minimumWindowWidth = SizeFromClientSize(new Size(
            minimumClientWidth,
            Math.Max(1, ClientSize.Height))).Width;

        return new Size(minimumWindowWidth, _mPx.MinimumHeight);
    }

    private Rectangle GetDpiAvailableBounds(Rectangle bounds)
    {
        Rectangle workingArea = Screen.FromRectangle(bounds).WorkingArea;

        // Match the Windows behavior you described: when the scaled window would
        // exceed the desktop, keep a small visual gap instead of touching edges.
        int gap = Math.Max(4, ScaleDip(8));
        Rectangle availableBounds = Rectangle.Inflate(workingArea, -gap, -gap);

        return availableBounds.Width > 0 && availableBounds.Height > 0
            ? availableBounds
            : workingArea;
    }

    private void ReapplyDpiMetrics(bool refreshContent, DpiWindowResizeState? resizeState = null)
    {
        int currentDpi = DeviceDpi;
        if (!refreshContent && currentDpi == _appliedDpi)
        {
            ApplyTreeViewDpiMetrics();
            QueueApplyLayoutMetrics();
            return;
        }

        RecalcMetrics();
        _appliedDpi = currentDpi;

        ApplyExplorerMinimumSize(resizeState?.SuggestedBounds);
        ApplyScaledDpiWindowBounds(resizeState);

        if (resizeState.HasValue)
            ResetSplitPaneResizeState();

        RebuildFonts();
        ApplyLayoutMetrics();
        ApplyImageListMetrics();
        ApplyTreeViewDpiMetrics();
        QueueApplyLayoutMetrics();

        if (!refreshContent)
            return;

        _presenter.ReloadTreeDriveRoots();
        _presenter.RequestRefreshCurrentView(RefreshReason.InternalRequest);
    }
}
