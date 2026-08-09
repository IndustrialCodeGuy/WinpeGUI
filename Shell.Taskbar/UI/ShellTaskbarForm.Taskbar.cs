using Shared.Shell.Interop;
using Shell.Taskbar.Shell;

namespace Shell.Taskbar.UI
{
    public sealed partial class ShellTaskbarForm : Form
    {
        // =====================================================================
        //  TASKBAR: WINDOW ENUMERATION + BUTTON REFRESH + FOCUS + REORDER
        // =====================================================================
        //
        // Purpose:
        // - Owns the runtime taskbar surface: enumerate eligible top-level windows,
        //   create/remove/update task buttons, apply icon/text sizing, track focus,
        //   and support drag-reorder behavior.
        //
        // Primary responsibilities here:
        // - RefreshTaskButtons():
        //     enumerate windows -> remove dead buttons -> create missing buttons
        //     -> preserve/update order -> apply current icon sizing/padding
        //     -> refresh title/icon text state -> refresh focus visuals.
        //
        // - ActivateOrRestore():
        //     task-button click behavior for minimized/restored/maximized windows,
        //     including hop animation and remembered pre-minimize max state.
        //
        // - Focus/foreground state:
        //     track the last foreground app hwnd and update task-button focused
        //     visuals so the taskbar mirrors normal Windows shell behavior.
        //
        // - Drag/reorder state:
        //     keep button order stable across refreshes while allowing user-driven
        //     reordering inside the task button panel.
        //
        // Debug entry points:
        // - RefreshTaskButtons():
        //     main runtime refresh loop and the best first stop for “missing task”,
        //     “wrong title”, “wrong icon”, “wrong order”, or “wrong focus” issues.
        //
        // - ActivateOrRestore():
        //     click behavior, minimize toggle behavior, restore/maximize behavior.
        //
        // - ReapplyTaskbarIconSizing(refreshIcons):
        //     taskbar icon px application to TaskWindowEnumerator and existing buttons;
        //     also the right place to inspect when DPI or icon-size changes look partial.
        //
        // - UpdateFocusVisual() / ClearFocusedAppState():
        //     focus border/background issues and stale focused-button state.
        //
        // Paired files while debugging:
        // - ShellTaskbarForm.Metrics.cs:
        //     fonts, padding, and control metrics originate there.
        // - ShellTaskbarForm.IconSizing.cs:
        //     icon-size setting -> taskbar/start-menu icon px policy.
        // - ShellTaskbarForm.BuildTaskbar.cs:
        //     task panel creation, button construction, drag event wiring.
        // - TaskButtonsPanel.cs:
        //     button panel layout and width distribution behavior.
        // - TaskWindowEmulator.cs / TaskWindow.cs:
        //     window/task model used by enumeration and task metadata refresh.
        //
        // Notes:
        // - _taskbarIconPx is the current desired button icon px derived from layout.
        // - _taskbarIconPxApplied is the last icon px actually pushed into live button
        //   state/caches so redundant icon rebuild work can be skipped.
        // - _lastTextState is used to avoid redoing text-fit/truncation work when the
        //   title text and available width have not changed.
        // - If focus visuals look wrong, inspect foreground hwnd updates first before
        //   changing button styling logic.
        //
        // =====================================================================

        #region Taskbar (fields)

        // ---------------- Task tracking ----------------
        private readonly Dictionary<IntPtr, BouncyTaskbarButton> _taskBtnByHwnd = [];
        private readonly Dictionary<IntPtr, bool> _lastNonMinimizedWasMax = [];
        private readonly Dictionary<IntPtr, (string Title, int AvailPx)> _lastTextState = [];
        private readonly Dictionary<BouncyTaskbarButton, string> _taskButtonToolTipText = [];
        private readonly TaskButtonToolTipPopup _taskButtonToolTip = new();
        private readonly System.Windows.Forms.Timer _taskButtonToolTipDelayTimer = new()
        {
            Interval = 500
        };
        private const int TaskButtonToolTipAutoPopDelayMs = 5000;
        private BouncyTaskbarButton? _pendingTaskButtonToolTipButton;
        private BouncyTaskbarButton? _visibleTaskButtonToolTipButton;

        private IntPtr _lastForegroundApp = IntPtr.Zero;
        private IntPtr _lastFocusedHwnd = IntPtr.Zero;

        private bool _refreshing;

        // ---------------- Drag/reorder ----------------
        private BouncyTaskbarButton _dragBtn;
        private Point _dragMouseDown;
        private bool _dragging;

        // ---------------- Sizing cache ----------------
        private int _lastBtnCount = -1;
        private int _lastTaskPanelWidth = -1;
        private int _lastTaskPanelHeight = -1;

        // Taskbar icon sizing cache (applies to TaskWindowEnumerator + buttons)
        private int _taskbarIconPx = -1;
        private int _taskbarIconPxApplied = -1;

        #endregion

        #region Taskbar (methods)

        private void RefreshTaskButtons()
        {
            if (_refreshing) return;

            if (_taskButtons == null || _taskButtons.IsDisposed || IsDisposed || Disposing)
                return;

            _refreshing = true;

            try
            {
                HashSet<IntPtr> excludedWindows = CollectExcludedTaskWindowHandles();

                var windows = TaskWindowEnumerator.GetTaskWindows(excludedWindows);

                HashSet<IntPtr> keep = new(windows.Count);
                                foreach (var window in windows)
                   keep.Add(window.Hwnd);

                // Hosted taskbar note:
                // The taskbar and Explorer windows may now live in the same process,
                // so do not ignore windows just because their PID matches this process.
                // If the current foreground HWND is a task-window HWND, mark it focused.
                // If the foreground HWND belongs to the taskbar/start/menu surface, leave
                // the previous app focus alone.
                var fgNow = TaskWindowEnumerator.GetForegroundWindowSafe();
                if (fgNow != IntPtr.Zero && keep.Contains(fgNow))
                    _lastForegroundApp = fgNow;

                List<IntPtr> dead = [];
                foreach (IntPtr hwnd in _taskBtnByHwnd.Keys)
                {
                    if (!keep.Contains(hwnd))
                        dead.Add(hwnd);
                }

                bool suspended = false;

                try
                {
                    _taskButtons.SuspendLayout();
                    suspended = true;

                    foreach (var hwnd in dead)
                    {
                        _taskBtnByHwnd.TryGetValue(hwnd, out var btn);

                        // Remove bookkeeping first
                        _taskBtnByHwnd.Remove(hwnd);
                        _lastNonMinimizedWasMax.Remove(hwnd);
                        _lastTextState.Remove(hwnd);

                        if (btn == null || btn.IsDisposed)
                        {
                            TaskWindowEnumerator.TryRemoveCachedIcon(hwnd);
                            continue;
                        }

                        var cms = btn.ContextMenuStrip;
                        btn.ContextMenuStrip = null;
                        ClearTaskButtonToolTip(btn);

                        _taskButtons.Controls.Remove(btn);

                        // Detach the live button image before disposing any fallback
                        // cached bitmap that may be the same Image instance.
                        btn.Image = null;
                        TaskWindowEnumerator.TryRemoveCachedIcon(hwnd);

                        try { cms?.Close(); } catch { }
                        try { cms?.Dispose(); } catch { }

                        try { btn.Dispose(); } catch { }
                    }

                    if (_lastForegroundApp != IntPtr.Zero &&
                        !TaskWindowEnumerator.IsWindow(_lastForegroundApp))
                    {
                        _lastForegroundApp = IntPtr.Zero;
                    }

                    foreach (var w in windows)
                    {
                        if (!_taskBtnByHwnd.TryGetValue(w.Hwnd, out var btnNew))
                        {
                            btnNew = CreateTaskButton(w.Hwnd);
                            _taskBtnByHwnd[w.Hwnd] = btnNew;
                            _taskButtons.Controls.Add(btnNew);

                            // Apply font/metrics after the button is parented.  A newly
                            // created control can receive WinForms' per-monitor DPI scaling
                            // while it is being parented/handle-created; assigning the shell
                            // owned taskbar metrics afterward keeps new buttons consistent
                            // with the already-open buttons updated by ApplyLayoutMetrics().
                            ApplyTaskButtonMetrics(btnNew);
                        }
                    }

                    if (_forceIconOnlyApplied != _forceIconOnly)
                    {
                        SetTaskIconOnly(_forceIconOnly);
                        _forceIconOnlyApplied = _forceIconOnly;
                    }

                    int panelW = _taskButtons.ClientSize.Width;
                    int panelH = _taskButtons.ClientSize.Height;

                    if (windows.Count != _lastBtnCount ||
                        panelW != _lastTaskPanelWidth ||
                        panelH != _lastTaskPanelHeight)
                    {
                        ApplyTaskButtonSizing();

                        _lastBtnCount = windows.Count;
                        _lastTaskPanelWidth = panelW;
                        _lastTaskPanelHeight = panelH;
                    }
                }
                finally
                {
                    if (suspended)
                        _taskButtons.ResumeLayout(true);
                }

                var fg = _lastForegroundApp;

                if (fg != _lastFocusedHwnd)
                {
                    UpdateFocusVisual(_lastFocusedHwnd, false);
                    UpdateFocusVisual(fg, true);
                    _lastFocusedHwnd = fg;

                    // Don't override keyboard focus while user is interacting with taskbar
                    if (!this.ContainsFocus)
                    {
                        if (fg != IntPtr.Zero && _taskBtnByHwnd.TryGetValue(fg, out var btn) && btn.CanFocus)
                            this.ActiveControl = btn;
                        else
                            this.ActiveControl = null;
                    }
                }

                foreach (var w in windows)
                {
                    if (!_taskBtnByHwnd.TryGetValue(w.Hwnd, out var bb))
                        continue;

                    // Ensure icon first (affects TextAvailablePx)
                    if (bb.Image == null)
                        UpdateButtonIcon(w.Hwnd, bb);

                    string newTitle = w.Title ?? "";
                    UpdateTaskButtonToolTip(bb, newTitle);

                    // Skip all text logic when icon-only
                    if (bb.DisplayMode == BouncyTaskbarButton.TaskButtonDisplayMode.IconOnly)
                        continue;

                    int textAvailPx = bb.TextAvailablePx;

                    bool needsUpdate =
                        !_lastTextState.TryGetValue(w.Hwnd, out var last) ||
                        last.AvailPx != textAvailPx ||
                        !string.Equals(last.Title, newTitle, StringComparison.Ordinal);

                    if (needsUpdate)
                    {
                        bb.TryUpdateDisplayedTitle(newTitle);
                        _lastTextState[w.Hwnd] = (newTitle, textAvailPx);
                    }
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        private HashSet<IntPtr> CollectExcludedTaskWindowHandles()
        {
            HashSet<IntPtr> excluded = new();

            void Add(IntPtr hwnd)
            {
                if (hwnd != IntPtr.Zero)
                    excluded.Add(hwnd);
            }

            void AddToolStrip(ToolStrip? toolStrip)
            {
                if (toolStrip == null || toolStrip.IsDisposed || !toolStrip.IsHandleCreated)
                    return;

                Add(toolStrip.Handle);
            }

            Add(Handle);
            if (_taskButtonToolTip.IsHandleCreated)
                Add(_taskButtonToolTip.Handle);
            AddToolStrip(_startMenu);
            AddToolStrip(_startItemCtx);
            AddToolStrip(_activeSubMenu);

            return excluded;
        }

        private void UpdateTaskButtonToolTip(BouncyTaskbarButton button, string fullTitle)
        {
            if (button == null || button.IsDisposed)
                return;

            string text = string.IsNullOrWhiteSpace(fullTitle) ? string.Empty : fullTitle;

            if (text.Length == 0)
            {
                ClearTaskButtonToolTip(button);
                return;
            }

            if (_taskButtonToolTipText.TryGetValue(button, out string? oldText) &&
                string.Equals(oldText, text, StringComparison.Ordinal))
            {
                return;
            }

            _taskButtonToolTipText[button] = text;

            if (ReferenceEquals(_visibleTaskButtonToolTipButton, button) &&
                button.ClientRectangle.Contains(button.PointToClient(Cursor.Position)))
            {
                ShowTaskButtonToolTip(button);
            }
        }

        private void StartTaskButtonToolTipDelay(BouncyTaskbarButton button)
        {
            if (button == null || button.IsDisposed || !button.IsHandleCreated)
                return;

            if (!_taskButtonToolTipText.TryGetValue(button, out string? text) ||
                string.IsNullOrWhiteSpace(text))
            {
                HideTaskButtonToolTip(button);
                return;
            }

            if (_visibleTaskButtonToolTipButton != null &&
                !ReferenceEquals(_visibleTaskButtonToolTipButton, button))
            {
                HideTaskButtonToolTip();
            }

            if (ReferenceEquals(_pendingTaskButtonToolTipButton, button) &&
                _taskButtonToolTipDelayTimer.Enabled)
            {
                return;
            }

            if (ReferenceEquals(_visibleTaskButtonToolTipButton, button))
                return;

            _pendingTaskButtonToolTipButton = button;
            _taskButtonToolTipDelayTimer.Stop();
            _taskButtonToolTipDelayTimer.Start();
        }

        private void CancelTaskButtonToolTipDelay(BouncyTaskbarButton? button = null)
        {
            if (button != null &&
                _pendingTaskButtonToolTipButton != null &&
                !ReferenceEquals(_pendingTaskButtonToolTipButton, button))
            {
                return;
            }

            _taskButtonToolTipDelayTimer.Stop();
            _pendingTaskButtonToolTipButton = null;
        }

        private void ArmTaskButtonToolTipIfNeeded(BouncyTaskbarButton button)
        {
            if (ReferenceEquals(_pendingTaskButtonToolTipButton, button) ||
                ReferenceEquals(_visibleTaskButtonToolTipButton, button))
            {
                return;
            }

            StartTaskButtonToolTipDelay(button);
        }

        private void TaskButtonToolTipDelayTimer_Tick(object? sender, EventArgs e)
        {
            _taskButtonToolTipDelayTimer.Stop();

            BouncyTaskbarButton? button = _pendingTaskButtonToolTipButton;
            _pendingTaskButtonToolTipButton = null;

            if (button == null || button.IsDisposed || !button.IsHandleCreated)
                return;

            if (!button.ClientRectangle.Contains(button.PointToClient(Cursor.Position)))
                return;

            ShowTaskButtonToolTip(button);
        }

        private void ShowTaskButtonToolTip(BouncyTaskbarButton button)
        {
            if (button == null || button.IsDisposed || !button.IsHandleCreated)
                return;

            if (!_taskButtonToolTipText.TryGetValue(button, out string? text) ||
                string.IsNullOrWhiteSpace(text))
            {
                HideTaskButtonToolTip(button);
                return;
            }

            CancelTaskButtonToolTipDelay(button);

            Rectangle buttonScreenBounds = button.RectangleToScreen(button.ClientRectangle);
            Rectangle taskbarScreenBounds = RectangleToScreen(ClientRectangle);
            Rectangle screenBounds = Screen.FromControl(button).WorkingArea;

            _visibleTaskButtonToolTipButton = button;
            _taskButtonToolTip.ShowForTaskButton(
                text,
                buttonScreenBounds,
                taskbarScreenBounds,
                screenBounds,
                Scale(6),
                TaskButtonToolTipAutoPopDelayMs);
        }

        private void HideTaskButtonToolTip(BouncyTaskbarButton? button = null)
        {
            CancelTaskButtonToolTipDelay(button);

            if (button != null &&
                _visibleTaskButtonToolTipButton != null &&
                !ReferenceEquals(_visibleTaskButtonToolTipButton, button))
            {
                return;
            }

            try { _taskButtonToolTip.HideTip(); } catch { }
            _visibleTaskButtonToolTipButton = null;
        }

        private void ClearTaskButtonToolTip(BouncyTaskbarButton button)
        {
            if (button == null)
                return;

            _taskButtonToolTipText.Remove(button);
            HideTaskButtonToolTip(button);
        }

        private void ApplyTaskButtonsPanelMetrics()
        {
            if (_taskButtons == null || _taskButtons.IsDisposed)
                return;

            int innerX = Math.Max(0, _mPx.BarPadX / 2);
            if ((innerX & 1) == 1) innerX -= 1;

            _taskButtons.SetMetrics(
                (_taskbarIconPx > 0) ? _taskbarIconPx : GetSmallIconPxFromLayout(),
                innerX,
                _mPx.TaskBtnGapX);
        }

        private void ApplyTaskButtonMetrics(BouncyTaskbarButton b)
        {
            if (b == null || b.IsDisposed)
                return;

            if (!ReferenceEquals(b.Font, _taskButtonFont))
                b.Font = _taskButtonFont;

            Padding padding = new(_mPx.TaskBtnPadX, _mPx.TaskBtnPadY, _mPx.TaskBtnPadX, _mPx.TaskBtnPadY);
            if (b.Padding != padding)
                b.Padding = padding;

            b.VisualOuterPadX = _mPx.TaskBtnGapX;
            b.IconTextGapPx = _mPx.IconTextGapX;

            if (b.HopOffsetPx != _mPx.HopOffsetY)
                b.HopOffsetPx = _mPx.HopOffsetY;

            b.IconBasePx = _taskbarIconPx;
        }

        private void ReapplyTaskbarIconSizing(bool refreshIcons)
        {
            int px = GetSmallIconPxFromLayout();
            bool sizeChanged = (_taskbarIconPx != px);

            _taskbarIconPx = px;
            bool detachedLiveImages = false;

            if (_taskbarIconPxApplied != px)
            {
                if (refreshIcons)
                {
                    DetachLiveTaskbarImages();
                    detachedLiveImages = true;
                }

                TaskWindowEnumerator.SetTaskbarIconSize(px);
                _taskbarIconPxApplied = px;
            }

            foreach (var bb in _taskBtnByHwnd.Values)
                if (!bb.IsDisposed)
                    bb.IconBasePx = px;

            if (_taskBtnMode == BouncyTaskbarButton.TaskButtonDisplayMode.IconOnly)
            {
                _lastBtnCount = -1;
                _lastTaskPanelWidth = -1;
                _lastTaskPanelHeight = -1;
            }

            if (!refreshIcons)
                return;

            if (sizeChanged)
            {
                if (!detachedLiveImages)
                    DetachLiveTaskbarImages();

                _lastTextState.Clear();
            }

            // Ensure start icon exists even if size didn't change.
            ApplyStartButtonIcon();
        }

        private void DetachLiveTaskbarImages()
        {
            if (_startButton != null && !_startButton.IsDisposed)
            {
                _startButton.Image = null;
                _startButton.PressedImage = null;
            }

            foreach (var b in _taskBtnByHwnd.Values)
            {
                if (!b.IsDisposed)
                    b.Image = null;
            }
        }

        private void UpdateButtonIcon(IntPtr hwnd, BouncyTaskbarButton btn)
        {
            if (hwnd == IntPtr.Zero || btn.IsDisposed) return;

            var desired = TaskWindowEnumerator.GetTaskbarIcon(hwnd);

            if (!ReferenceEquals(btn.Image, desired))
            {
                btn.Image = desired;
            }
        }

        private void ApplyTaskButtonDisplayMode()
        {
            bool anyChanged = false;

            foreach (var bb in _taskBtnByHwnd.Values)
            {
                if (bb.IsDisposed) continue;

                if (_forceIconOnly)
                {
                    if (bb.AutoIconModeEnabled != false) { bb.AutoIconModeEnabled = false; anyChanged = true; }
                    if (bb.DisplayMode != BouncyTaskbarButton.TaskButtonDisplayMode.IconOnly)
                    { bb.DisplayMode = BouncyTaskbarButton.TaskButtonDisplayMode.IconOnly; anyChanged = true; }
                }
                else
                {
                    if (bb.AutoIconModeEnabled != true) { bb.AutoIconModeEnabled = true; anyChanged = true; }
                    if (bb.DisplayMode != BouncyTaskbarButton.TaskButtonDisplayMode.Label)
                    { bb.DisplayMode = BouncyTaskbarButton.TaskButtonDisplayMode.Label; anyChanged = true; }
                }
            }

            if (anyChanged)
                _lastTextState.Clear();
        }

        private void SetTaskIconOnly(bool iconOnly)
        {
            if (_forceIconOnly == iconOnly)
            {
                var nextSame = iconOnly
                    ? BouncyTaskbarButton.TaskButtonDisplayMode.IconOnly
                    : BouncyTaskbarButton.TaskButtonDisplayMode.Label;

                if (_taskBtnMode == nextSame)
                    return; // nothing to do
            }

            _forceIconOnly = iconOnly;

            var next = iconOnly
                ? BouncyTaskbarButton.TaskButtonDisplayMode.IconOnly
                : BouncyTaskbarButton.TaskButtonDisplayMode.Label;

            if (_taskBtnMode == next)
            {
                ApplyTaskButtonDisplayMode();
                return;
            }

            _taskBtnMode = next;

            ApplyTaskButtonDisplayMode();

            _lastBtnCount = -1;
            _lastTaskPanelWidth = -1;
            _lastTaskPanelHeight = -1;
        }

        private BouncyTaskbarButton CreateTaskButton(IntPtr hwndLocal)
        {
            var btn = new BouncyTaskbarButton
            {
                Margin = new Padding(0),
                DisplayMode = _taskBtnMode,
                TabStop = true
            };

            btn.Click += (s, e) =>
            {
                if (_dragging) return;
                ActivateOrRestore(hwndLocal);
            };

            btn.MouseEnter += (s, e) => ArmTaskButtonToolTipIfNeeded(btn);
            btn.MouseLeave += (s, e) => HideTaskButtonToolTip(btn);
            btn.MouseDown += (s, e) => HideTaskButtonToolTip(btn);

            btn.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _dragBtn = btn;
                _dragMouseDown = e.Location;
                _dragging = false;
            };

            btn.MouseMove += (s, e) =>
            {
                if ((Control.MouseButtons & MouseButtons.Left) == 0)
                {
                    ArmTaskButtonToolTipIfNeeded(btn);
                    return;
                }

                if (_dragBtn != btn) return;

                if (!_dragging)
                {
                    int dx = Math.Abs(e.X - _dragMouseDown.X);
                    int dy = Math.Abs(e.Y - _dragMouseDown.Y);
                    if (dx < SystemInformation.DragSize.Width / 2 &&
                        dy < SystemInformation.DragSize.Height / 2)
                        return;

                    _dragging = true;

                    btn.BeginDragVisual();

                    try
                    {
                        var data = new DataObject();
                        data.SetData(typeof(BouncyTaskbarButton), btn);
                        btn.DoDragDrop(data, DragDropEffects.Move);
                    }
                    finally
                    {
                        btn.EndDragVisual();
                        if (_dragBtn == btn) _dragBtn = null;
                        _dragging = false;
                        if (btn.ClientRectangle.Contains(btn.PointToClient(Cursor.Position)))
                            ArmTaskButtonToolTipIfNeeded(btn);
                    }
                }
            };

            btn.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _dragBtn = null;
                _dragging = false;
                btn.EndDragVisual();
            };

            btn.ContextMenuStrip = BuildTaskContextMenu(hwndLocal);
            return btn;
        }

        private void ApplyTaskContextMenuFonts()
        {
            foreach (var button in _taskBtnByHwnd.Values)
            {
                ContextMenuStrip? menu = button.ContextMenuStrip;
                if (menu == null || menu.IsDisposed)
                    continue;

                menu.Font = _taskCtxGlyphFont;

                foreach (ToolStripItem item in menu.Items)
                    item.Font = _taskCtxGlyphFont;
            }
        }

        private ContextMenuStrip BuildTaskContextMenu(IntPtr hwndLocal)
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false
            };

            var miRestore = new ToolStripMenuItem("❐     Restore") { Font = _taskCtxGlyphFont };
            var miMinimize = new ToolStripMenuItem("─      Minimize") { Font = _taskCtxGlyphFont, Margin = new Padding(2, 0, 0, 0) };
            var miMaximize = new ToolStripMenuItem("▢     Maximize") { Font = _taskCtxGlyphFont, Margin = new Padding(1, 0, 0, 0) };
            var miClose = new ToolStripMenuItem("✕     Close") { Font = _taskCtxGlyphFont, Margin = new Padding(1, 0, 0, 0) };

            miRestore.Click += (s, e) =>
            {
                if (!TaskWindowEnumerator.IsWindow(hwndLocal)) return;

                bool minimized = TaskWindowEnumerator.IsMinimized(hwndLocal);
                bool maximized = TaskWindowEnumerator.IsMaximized(hwndLocal);

                if (minimized)
                {
                    bool wasMax = false;
                    _lastNonMinimizedWasMax.TryGetValue(hwndLocal, out wasMax);

                    TaskWindowEnumerator.Restore(hwndLocal);
                    if (wasMax)
                        TaskWindowEnumerator.Maximize(hwndLocal);

                    TaskWindowEnumerator.Activate(hwndLocal);
                    _lastForegroundApp = hwndLocal;
                    return;
                }

                if (maximized)
                {
                    TaskWindowEnumerator.Restore(hwndLocal);
                    TaskWindowEnumerator.Activate(hwndLocal);
                    _lastForegroundApp = hwndLocal;

                    _lastNonMinimizedWasMax[hwndLocal] = false;
                }
            };

            miMinimize.Click += (s, e) =>
            {
                if (TaskWindowEnumerator.IsWindow(hwndLocal) && !TaskWindowEnumerator.IsMinimized(hwndLocal))
                    TaskWindowEnumerator.Minimize(hwndLocal);
            };

            miMaximize.Click += (s, e) =>
            {
                if (TaskWindowEnumerator.IsWindow(hwndLocal) && !TaskWindowEnumerator.IsMaximized(hwndLocal))
                {
                    TaskWindowEnumerator.Maximize(hwndLocal);
                    TaskWindowEnumerator.Activate(hwndLocal);
                    _lastForegroundApp = hwndLocal;
                }
            };

            miClose.Click += (s, e) =>
            {
                if (TaskWindowEnumerator.IsWindow(hwndLocal))
                    TaskWindowEnumerator.Close(hwndLocal);
            };

            menu.Opening += (s, e) =>
            {
                HideTaskButtonToolTip();

                bool exists = TaskWindowEnumerator.IsWindow(hwndLocal);
                bool minimized = exists && TaskWindowEnumerator.IsMinimized(hwndLocal);
                bool maximized = exists && TaskWindowEnumerator.IsMaximized(hwndLocal);

                miRestore.Enabled = exists && (minimized || maximized);
                miMinimize.Enabled = exists && !minimized;
                miMaximize.Enabled = exists && !maximized;
                miClose.Enabled = exists;
            };

            menu.Items.Add(miRestore);
            menu.Items.Add(miMinimize);
            menu.Items.Add(miMaximize);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miClose);

            return menu;
        }

        private void CloseAndDisposeTaskContextMenus()
        {
            HideTaskButtonToolTip();

            foreach (BouncyTaskbarButton btn in _taskBtnByHwnd.Values.ToArray())
            {
                if (btn == null || btn.IsDisposed)
                    continue;

                ContextMenuStrip? menu = btn.ContextMenuStrip;
                if (menu == null)
                    continue;

                btn.ContextMenuStrip = null;

                try { menu.Close(ToolStripDropDownCloseReason.CloseCalled); } catch { }
                try { menu.Dispose(); } catch { }
            }
        }

        private void RebuildTaskContextMenus()
        {
            foreach (KeyValuePair<IntPtr, BouncyTaskbarButton> pair in _taskBtnByHwnd.ToArray())
            {
                BouncyTaskbarButton btn = pair.Value;
                if (btn == null || btn.IsDisposed)
                    continue;

                if (btn.ContextMenuStrip == null || btn.ContextMenuStrip.IsDisposed)
                    btn.ContextMenuStrip = BuildTaskContextMenu(pair.Key);
            }
        }


        private void ActivateOrRestore(IntPtr hwnd)
        {
            try
            {
                if (!TaskWindowEnumerator.IsWindow(hwnd))
                    return;

                _taskBtnByHwnd.TryGetValue(hwnd, out var bb);

                if (_lastForegroundApp == hwnd &&
                    !TaskWindowEnumerator.IsMinimized(hwnd))
                {
                    _lastNonMinimizedWasMax[hwnd] =
                        TaskWindowEnumerator.IsMaximized(hwnd);

                    this.ActiveControl = null;

                    bb?.HopDown();

                    TaskWindowEnumerator.Minimize(hwnd);
                    return;
                }

                if (TaskWindowEnumerator.IsMinimized(hwnd))
                {
                    bool wasMax = false;
                    _lastNonMinimizedWasMax.TryGetValue(hwnd, out wasMax);

                    bb?.HopUp();

                    TaskWindowEnumerator.Restore(hwnd);

                    if (wasMax)
                        TaskWindowEnumerator.Maximize(hwnd);
                }

                TaskWindowEnumerator.Activate(hwnd);
                UpdateFocusVisual(hwnd, true);
                _lastForegroundApp = hwnd;
            }
            catch
            {
            }
        }

        private void UpdateFocusVisual(IntPtr hwnd, bool focused)
        {
            if (hwnd == IntPtr.Zero) return;

            if (_taskBtnByHwnd.TryGetValue(hwnd, out var bb) && !bb.IsDisposed)
            {
                bb.ApplyFocusState(focused);
            }
        }

        private void ClearFocusedAppState()
        {
            if (_lastFocusedHwnd != IntPtr.Zero)
                UpdateFocusVisual(_lastFocusedHwnd, false);

            _lastFocusedHwnd = IntPtr.Zero;
            _lastForegroundApp = IntPtr.Zero;
        }

        private void ApplyTaskButtonSizing()
        {
            if (_taskButtons == null || _taskButtons.IsDisposed) return;

            int count = _taskButtons.Controls.Count;
            if (count <= 0) return;

            int avail = _taskButtons.ClientSize.Width;
            if (avail <= 0) return;

            if (_taskBtnMode == BouncyTaskbarButton.TaskButtonDisplayMode.IconOnly)
            {
                int icon = (_taskbarIconPx > 0) ? _taskbarIconPx : GetSmallIconPxFromLayout();

                int w = icon
                      + (_mPx.TaskBtnPadX * 4)
                      + (_mPx.TaskBtnGapX * 2);

                foreach (Control c in _taskButtons.Controls)
                {
                    if (c is BouncyTaskbarButton bb && !bb.IsDisposed)
                    {
                        bb.AutoIconModeEnabled = false;
                        bb.DisplayMode = BouncyTaskbarButton.TaskButtonDisplayMode.IconOnly;
                        bb.Width = w;
                    }
                }

                return;
            }

            int maxW = Math.Max(1, _mPx.TaskBtnMaxW);
            int baseW = avail / count;

            BouncyTaskbarButton? sample = null;
            foreach (Control c in _taskButtons.Controls)
            {
                sample = c as BouncyTaskbarButton;
                if (sample != null && !sample.IsDisposed) break;
            }

            if (!_forceIconOnly && sample != null && baseW > 0 && baseW < sample.MinLabelButtonWidthPx)
            {
                int icon = (_taskbarIconPx > 0) ? _taskbarIconPx : GetSmallIconPxFromLayout();

                int w = icon
                      + (_mPx.TaskBtnPadX * 4)
                      + (_mPx.TaskBtnGapX * 2);

                foreach (Control c in _taskButtons.Controls)
                {
                    if (c is not BouncyTaskbarButton bb || bb.IsDisposed)
                        continue;

                    bb.AutoIconModeEnabled = false;
                    bb.DisplayMode = BouncyTaskbarButton.TaskButtonDisplayMode.IconOnly;
                    bb.Width = w;
                }

                _taskButtons.PerformLayout();
                return;
            }

            bool compressing = baseW <= maxW;

            baseW = Math.Max(1, Math.Min(maxW, baseW));

            int used = baseW * count;
            int rem = avail - used;

            if (!compressing) rem = 0;

            if (rem < 0) rem = 0;
            if (rem > count) rem = count;

            int i = 0;
            foreach (Control c in _taskButtons.Controls)
            {
                if (c is not BouncyTaskbarButton bb || bb.IsDisposed)
                    continue;

                bb.AutoIconModeEnabled = false;
                bb.DisplayMode = BouncyTaskbarButton.TaskButtonDisplayMode.Label;

                bb.Width = baseW + (i < rem ? 1 : 0);
                i++;
            }

            _taskButtons.PerformLayout();
        }


        private sealed class TaskButtonToolTipPopup : Form
        {
            private const int WM_MOUSEACTIVATE = 0x0021;
            private const int MA_NOACTIVATE = 3;

            private readonly Label _label;
            private readonly System.Windows.Forms.Timer _autoHideTimer = new();
            private string _currentText = string.Empty;

            public TaskButtonToolTipPopup()
            {
                AutoScaleMode = AutoScaleMode.None;
                AutoScaleDimensions = new SizeF(96f, 96f);
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                ControlBox = false;
                MinimizeBox = false;
                MaximizeBox = false;
                TopMost = true;
                BackColor = TaskbarTheme.ShellBack;
                Padding = new Padding(8, 5, 8, 5);

                _label = new Label
                {
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = TaskbarTheme.ShellBack,
                    ForeColor = TaskbarTheme.TextColor,
                    Font = SystemFonts.StatusFont,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                    UseMnemonic = false
                };

                Controls.Add(_label);

                _autoHideTimer.Tick += (s, e) => HideTip();
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

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_MOUSEACTIVATE)
                {
                    m.Result = (IntPtr)MA_NOACTIVATE;
                    return;
                }

                base.WndProc(ref m);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.ControlDark, ButtonBorderStyle.Solid);
            }

            public void ShowForTaskButton(
                string text,
                Rectangle buttonScreenBounds,
                Rectangle taskbarScreenBounds,
                Rectangle screenBounds,
                int gap,
                int autoPopDelayMs)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    HideTip();
                    return;
                }

                if (!string.Equals(_currentText, text, StringComparison.Ordinal))
                {
                    _currentText = text;
                    _label.Text = text;
                    Size preferred = _label.GetPreferredSize(Size.Empty);
                    ClientSize = new Size(
                        preferred.Width + Padding.Left + Padding.Right,
                        preferred.Height + Padding.Top + Padding.Bottom);
                }

                int x = buttonScreenBounds.Left + ((buttonScreenBounds.Width - Width) / 2);
                int y = taskbarScreenBounds.Top - Height - gap;

                int maxX = Math.Max(screenBounds.Left, screenBounds.Right - Width);
                x = Math.Max(screenBounds.Left, Math.Min(x, maxX));

                if (y < screenBounds.Top)
                    y = taskbarScreenBounds.Bottom + gap;

                Bounds = new Rectangle(x, y, Width, Height);

                if (!Visible)
                    Show();

                _autoHideTimer.Stop();
                if (autoPopDelayMs > 0)
                {
                    _autoHideTimer.Interval = autoPopDelayMs;
                    _autoHideTimer.Start();
                }
            }

            public void HideTip()
            {
                _autoHideTimer.Stop();

                if (Visible)
                    Hide();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    try { _autoHideTimer.Stop(); } catch { }
                    try { _autoHideTimer.Dispose(); } catch { }
                }

                base.Dispose(disposing);
            }
        }


        #endregion
    }
}
