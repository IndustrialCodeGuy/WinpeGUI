using Shared.Shell.Interop;
using Shared.Shell.Utilities;
using System.Diagnostics;
using Shell.Taskbar.Interop;
using Shell.Taskbar.Shell;

namespace Shell.Taskbar.UI
{
    public sealed partial class ShellTaskbarForm : Form
    {
        private const int StartButtonIconIndex = ShellOwnedWindowIcons.TaskbarIconIndex;
        private const int StartButtonPressedIconIndex = 251;

        // =====================================================================
        //  SHELLFORM (MAIN): LIFECYCLE + ORCHESTRATION + APPBAR + SHELL STATE
        // =====================================================================
        //
        // Purpose:
        // - Owns the main shell form lifecycle and the top-level orchestration for
        //   taskbar startup, WinPE desktop integration,
        //   DPI/layout reapplication, and shutdown/cleanup.
        //
        // Primary responsibilities here:
        // - Constructor startup order:
        //     detect WinPE -> initialize shared state/timers -> resolve metrics/fonts
        //     -> build UI surface -> apply layout -> dock/register AppBar
        //     -> wire start-menu / taskbar / desktop-shell behavior.
        //
        // - Shell-wide layout and DPI flow:
        //     OnDpiChanged() -> tear down transient start surfaces -> reapply metrics,
        //     fonts, bounds, icon sizing, taskbar sizing, AppBar docking, then rebuild
        //     any UI that depends on real post-scale sizes.
        //
        // - Shell-wide cleanup:
        //     stop timers, tear down hooks/menus/AppBar registrations, detach any
        //     transient WinPE desktop surface, and dispose shared resources cleanly.
        //
        // Debug entry points:
        // - ShellTaskbarForm() constructor:
        //     startup order, WinPE detection,
        //     initial metric application, timer wiring, first layout stabilization.
        //
        // - OnShown / initial shell bring-up:
        //     final taskbar refresh, AppBar registration, optional desktop overlay,
        //     and first start-menu/taskbar ready state.
        //
        // - OnDpiChanged():
        //     suggested bounds, transient menu teardown, then ReapplyLayout(true).
        //
        // - ReapplyLayout(rebuildStartMenu):
        //     RecalcMetrics (Metrics) -> ApplyLayoutMetrics (Metrics)
        //     -> force layout -> ReapplyTaskbarIconSizing (Taskbar)
        //     -> ApplyTaskButtonsPanelMetrics (Taskbar)
        //     -> EnsureAppBarDocked (here)
        //     -> optional RebuildStartMenu (StartMenu)
        //     -> reset taskbar sizing caches (Taskbar)
        //     -> RefreshTaskButtons (Taskbar).
        //
        // Paired files while debugging:
        // - ShellTaskbarForm.Metrics.cs:
        //     metric calculation, font creation/disposal, layout propagation, theme.
        // - ShellTaskbarForm.BuildTaskbar.cs:
        //     shell surface construction, start button/task panel wiring.
        // - ShellTaskbarForm.IconSizing.cs:
        //     icon-size policy and derived taskbar/start-menu icon px.
        // - ShellTaskbarForm.Taskbar.cs:
        //     task enumeration, button refresh, focus visuals, activation behavior.
        // - ShellTaskbarForm.StartMenu.cs:
        //     start menu build/runtime, submenu/context-loop behavior.
        // - ShellTaskbarForm.Dispose.cs:
        //     final cleanup for timers, hooks, AppBar, desktop form, shared resources.
        //
        // Notes:
        // - AppBar docking is applied after metrics are resolved so height and bounds
        //   reflect the final scaled layout.
        // - Start surfaces are torn down before DPI rebuilds to avoid stale scaled
        //   menus and cached image-size mismatches.
        //
        // =====================================================================

        // =====================================================================
        //  STATE (LIFECYCLE-OWNED)
        // =====================================================================

        #region State

        // ---------------- AppBar / sizing cache ----------------
        private AppBar? _appBar;
        private int _appBarHeightApplied = -1;

        // ---------------- WinPE manual work-area cache ----------------
        private bool _winPeManualWorkAreaActive;
        private Rectangle _winPeManualWorkAreaApplied = Rectangle.Empty;
        private Rectangle _winPeManualWorkAreaResetBounds = Rectangle.Empty;

        // ---------------- Check if running in WinPE ----------------
        private readonly bool _isWinPE;
        private readonly bool _showBitLockerManagerStartMenu;
        private readonly bool _showImagingManagerStartMenu;

        // ---------------- Start button / menu state ----------------
        private KeyboardHook? _kbHook;

        #endregion

        // =====================================================================
        //  LIFECYCLE / OVERRIDES
        // =====================================================================

        #region Lifecycle / Overrides

        public event EventHandler? OpenExplorerRequested;
        public event EventHandler? BitLockerManagerRequested;
        public event EventHandler? ImagingManagerRequested;
        public event EventHandler? ShutdownRequested;
        public event EventHandler? RebootRequested;
        private Icon? _windowIcon;

        public ShellTaskbarForm(bool showBitLockerManagerStartMenu = false, bool showImagingManagerStartMenu = false)
        {
            // The taskbar is manually scaled. Prevent WinForms from applying
            // an additional startup autoscale pass to child controls.
            AutoScaleMode = AutoScaleMode.None;
            AutoScaleDimensions = new SizeF(96f, 96f);

            Text = "WinPE Taskbar";
            _windowIcon = ShellOwnedWindowIcons.CreateWindowIcon(ShellOwnedWindowIcons.TaskbarIconIndex);
            if (_windowIcon != null)
                Icon = _windowIcon;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;

            // Force the top-level handle before calculating metrics/building child controls.
            // Without this, constructor-time DeviceDpi/control scaling can be wrong when
            // the shell starts above 100%, even though later DPI changes are correct.
            _ = Handle;

            // Check if running in WinPE
            _isWinPE = PlatformDetect.IsWinPE;
            _showBitLockerManagerStartMenu = showBitLockerManagerStartMenu;
            _showImagingManagerStartMenu = showImagingManagerStartMenu;

            // Startup settings...
            _iconSizeSetting = IconSizeSetting.Small;

            _taskButtonToolTipDelayTimer.Tick += TaskButtonToolTipDelayTimer_Tick;

            // Metrics first (fonts depend on this)
            RecalcMetrics();
            ApplyFormMetricsAndFonts();

            // Build UI surfaces
            BuildTaskbar();
            ApplyLayoutMetricsToControls();

            // Force first layout so sizes are real before menu/icon sizing
            PerformLayout();
            _taskbar?.PerformLayout();

            // Win-key hook
            try
            {
                _kbHook = new KeyboardHook();
                _kbHook.WinKeyTapped += () =>
                {
                    if (IsDisposed || Disposing) return;

                    if (InvokeRequired)
                        BeginInvoke(new Action(ToggleStartMenu));
                    else
                        ToggleStartMenu();
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _kbHook = null;
            }

            Shown += (s, e) =>
            {
                EnsureDesktopSurface();

                // Re-run the full layout once the form has a real handle/window DPI.
                // This avoids startup DPI mismatches where constructor-time metrics can
                // differ from the final AppBar/taskbar surface metrics.
                ReapplyLayout(rebuildStartMenu: true);
                StartTimers();
            };
        }

        protected override void WndProc(ref Message m)
        {
            bool handlePeDisplayChange = _isWinPE && m.Msg == WM_DISPLAYCHANGE;

            if (_appBar != null && _appBar.HandleWndProc(ref m))
                return;

            base.WndProc(ref m);

            if (handlePeDisplayChange)
                ReapplyLayoutForDisplayChange();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.F4))
            {
                if (Form.ActiveForm == this)
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            // Tear down ToolStrip surfaces before WinForms/native DPI handling can
            // remeasure them with stale fonts or image metrics. ReapplyLayout(true)
            // rebuilds the start menu and task context menus with the new DPI fonts.
            DisposeStartMenu();
            CloseAndDisposeTaskContextMenus();

            base.OnDpiChanged(e);

            Bounds = e.SuggestedRectangle;

            ReapplyLayout(rebuildStartMenu: true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;

                // Keep the taskbar out of normal task/app window enumeration.
                // This should keep PE Task Manager from listing it as a window
                // with minimize/maximize options.
                cp.ExStyle |= User32.WS_EX_TOOLWINDOW;
                cp.ExStyle &= ~User32.WS_EX_APPWINDOW;

                return cp;
            }
        }

        #endregion

        // =====================================================================
        //  APPBAR / DOCKING
        // =====================================================================

        #region AppBar / Work Area

        private const int WM_DISPLAYCHANGE = 0x007E;

        private void EnsureAppBarDocked()
        {
            if (IsDisposed || Disposing) return;

            int h = _mPx.TaskbarHeight;

            if (_isWinPE)
            {
                // WinPE does not reliably honor SHAppBarMessage work-area reservation.
                // Skip AppBar registration there, keep the same bottom-edge taskbar
                // bounds the AppBar path would have produced, and reserve the work
                // area manually with SPI_SETWORKAREA.
                if (_appBar != null)
                {
                    try { _appBar.Unregister(); } catch { }
                    _appBar = null;
                    _appBarHeightApplied = -1;
                }

                ApplyWinPeManualWorkArea(h);
                return;
            }

            // If already docked for this height, don't churn.
            if (_appBar != null && _appBarHeightApplied == h)
                return;

            try { _appBar?.Unregister(); } catch { }
            _appBar = null;
            _appBarHeightApplied = -1;

            try
            {
                _appBar = new AppBar(this, AppBarEdge.Bottom, h);
                _appBar.RegisterAndDock();
                _appBarHeightApplied = h;
            }
            catch
            {
                _appBar = null;
                _appBarHeightApplied = -1;
            }
        }

        private void ApplyWinPeManualWorkArea(int taskbarHeight)
        {
            if (!IsHandleCreated)
                return;

            Rectangle screen = Screen.FromHandle(Handle).Bounds;
            int height = Math.Min(Math.Max(20, taskbarHeight), Math.Max(1, screen.Height));

            Rectangle taskbarBounds = new(
                screen.Left,
                screen.Bottom - height,
                screen.Width,
                height);

            Rectangle workArea = new(
                screen.Left,
                screen.Top,
                screen.Width,
                Math.Max(0, screen.Height - height));

            if (Bounds != taskbarBounds)
                Bounds = taskbarBounds;

            if (!_winPeManualWorkAreaActive || _winPeManualWorkAreaApplied != workArea)
            {
                User32.RECT rc = ToRect(workArea);
                if (User32.SystemParametersInfo(User32.SPI_SETWORKAREA, 0, ref rc, User32.SPIF_SENDCHANGE))
                {
                    _winPeManualWorkAreaActive = true;
                    _winPeManualWorkAreaApplied = workArea;
                    _winPeManualWorkAreaResetBounds = screen;
                }
            }

            if (!TopMost)
                TopMost = true;
        }

        private void ResetWinPeManualWorkArea()
        {
            if (!_winPeManualWorkAreaActive)
                return;

            Rectangle resetBounds = IsHandleCreated
                ? Screen.FromHandle(Handle).Bounds
                : _winPeManualWorkAreaResetBounds;

            if (resetBounds.IsEmpty)
                resetBounds = _winPeManualWorkAreaResetBounds;

            if (!resetBounds.IsEmpty)
            {
                User32.RECT rc = ToRect(resetBounds);
                try { User32.SystemParametersInfo(User32.SPI_SETWORKAREA, 0, ref rc, User32.SPIF_SENDCHANGE); } catch { }
            }

            _winPeManualWorkAreaActive = false;
            _winPeManualWorkAreaApplied = Rectangle.Empty;
            _winPeManualWorkAreaResetBounds = Rectangle.Empty;
            _appBarHeightApplied = -1;
        }

        private void ReapplyLayoutForDisplayChange()
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed && !Disposing)
                        ReapplyLayout(rebuildStartMenu: false);
                }));
            }
            catch
            {
            }
        }

        private static User32.RECT ToRect(Rectangle rectangle)
        {
            return new User32.RECT
            {
                Left = rectangle.Left,
                Top = rectangle.Top,
                Right = rectangle.Right,
                Bottom = rectangle.Bottom
            };
        }

        #endregion

        // =====================================================================
        //  TOP-LEVEL REAPPLY / REBUILD ORCHESTRATION
        // =====================================================================

        #region Orchestration

        private void ReapplyLayout(bool rebuildStartMenu)
        {
            if (rebuildStartMenu)
            {
                // Anything backed by ToolStripItem.Font must be gone before
                // ApplyLayoutMetrics swaps and disposes the old DPI fonts.
                DisposeStartMenu();
                CloseAndDisposeTaskContextMenus();
            }

            RecalcMetrics();
            ApplyLayoutMetrics();

            PerformLayout();
            _taskbar?.PerformLayout();

            ReapplyTaskbarIconSizing(refreshIcons: true);
            ApplyTaskButtonsPanelMetrics();
            EnsureDesktopSurface();
            EnsureAppBarDocked();

            if (rebuildStartMenu)
                RebuildStartMenu();

            _lastBtnCount = -1;
            _lastTaskPanelWidth = -1;
            _lastTaskPanelHeight = -1;
            _lastTextState.Clear();

            RefreshTaskButtons();

            if (rebuildStartMenu)
                RebuildTaskContextMenus();
        }

        private void RebuildStartMenu()
        {
            DisposeStartMenu();

            // IMPORTANT: clear caches so old-size images don’t get reused
            Icons.ClearStartCaches();

            _startMenu = BuildStartMenu();
            _startMenuPreferredHeightPx = -1;
        }

        #endregion

        // =====================================================================
        //  BUTTON CHROME + START ICON
        // =====================================================================

        #region Button Chrome / Icons

        private void ApplyStartButtonIcon()
        {
            if (_startButton == null || _startButton.IsDisposed) return;

            int px = (_taskbarIconPx > 0) ? _taskbarIconPx : GetSmallIconPxFromLayout();

            _startButton.Image = Icons.FromTaskbarSystemDll("imageres.dll", StartButtonIconIndex, px);
            _startButton.PressedImage = Icons.FromTaskbarSystemDll("imageres.dll", StartButtonPressedIconIndex, px);

            _startButton.ImageAlign = ContentAlignment.MiddleCenter;
            _startButton.TextImageRelation = TextImageRelation.Overlay;

            _startButton.IconBasePx = px;
        }

        private void OpenFileExplorer()
        {
            OpenExplorerRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OpenBitLockerManager()
        {
            BitLockerManagerRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OpenImagingManager()
        {
            ImagingManagerRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RequestShutdown()
        {
            CloseStartSurfaces();
            ShutdownRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RequestReboot()
        {
            CloseStartSurfaces();
            RebootRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
