using Shell.Taskbar.Shell;

namespace Shell.Taskbar.UI
{
    public sealed partial class ShellTaskbarForm : Form
    {
        // =====================================================================
        //  DISPOSE / CLEANUP
        // =====================================================================
        //
        // Purpose:
        // - Centralized teardown for timers, menus, AppBar, hooks, and cached icons.
        //
        // Paired files (when debugging cleanup issues):
        // - ShellTaskbarForm.Timers.cs: timer fields are defined there.
        // - ShellTaskbarForm.StartMenu.cs: CloseStartSurfaces(), menu fields, image detach helpers.
        // - ShellTaskbarForm.Taskbar.cs: task button dictionaries and TaskWindowEnumerator caches.
        // - ShellTaskbarForm.Metrics.cs: fonts are created/disposed there, but final disposal is here.
        //
        // Notes:
        // - DetachToolStripImages is used to avoid disposing shared cached images.
        // - TaskWindowEnumerator cached icons are explicitly removed for each hwnd.
        //
        // =====================================================================

        #region Dispose

        // Cleanup
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _kbHook?.Dispose(); } catch { }
                _kbHook = null;

                try { _refreshTimer.Stop(); } catch { }
                try { _clockTimer.Stop(); } catch { }
                try { _taskButtonToolTipDelayTimer.Stop(); } catch { }

                try { _appBar?.Unregister(); } catch { }
                _appBar = null;
                _appBarHeightApplied = -1;
                try { ResetWinPeManualWorkArea(); } catch { }

                try { DisposeDesktopSurface(); } catch { }
                try { CloseStartSurfaces(); } catch { }

                try
                {
                    if (_activeSubMenu != null && !_activeSubMenu.IsDisposed)
                        DetachToolStripImages(_activeSubMenu.Items);
                }
                catch { }

                try
                {
                    if (_startMenu != null && !_startMenu.IsDisposed)
                        DetachToolStripImages(_startMenu.Items);
                }
                catch { }

                try { _startMenu?.Dispose(); } catch { }
                _startMenu = null;

                if (_taskButtons != null && !_taskButtons.IsDisposed)
                {
                    foreach (var b in _taskBtnByHwnd.Values)
                    {
                        if (b != null && !b.IsDisposed)
                        {
                            var cms = b.ContextMenuStrip;
                            b.ContextMenuStrip = null;
                            try { ClearTaskButtonToolTip(b); } catch { }
                            try { cms?.Close(); } catch { }
                            try { cms?.Dispose(); } catch { }

                            try { _taskButtons.Controls.Remove(b); } catch { }
                            try { b.Image = null; } catch { }
                            try { b.Dispose(); } catch { }
                        }
                    }
                }

                try
                {
                    foreach (var hwnd in _taskBtnByHwnd.Keys.ToList())
                    {
                        TaskWindowEnumerator.TryRemoveCachedIcon(hwnd);
                    }
                }
                catch { }

                _taskBtnByHwnd.Clear();
                _lastNonMinimizedWasMax.Clear();
                _lastTextState.Clear();
                _taskButtonToolTipText.Clear();
                _pendingTaskButtonToolTipButton = null;
                _visibleTaskButtonToolTipButton = null;

                try
                {
                    if (ReferenceEquals(Icon, _windowIcon))
                        Icon = null;

                    _windowIcon?.Dispose();
                }
                catch { }
                _windowIcon = null;

                try { _startButton.Image = null; } catch { }
                try { _startButton.PressedImage = null; } catch { }
                try { TaskWindowEnumerator.ClearIconCaches(); } catch { }
                try { Shared.Shell.Utilities.Icons.ClearStartCaches(); } catch { }

                try { _refreshTimer.Dispose(); } catch { }
                try { _clockTimer.Dispose(); } catch { }
                try { _taskButtonToolTipDelayTimer.Dispose(); } catch { }

                try { _taskButtonToolTip.HideTip(); } catch { }
                try { _taskButtonToolTip.Dispose(); } catch { }

                // Do not dispose layout fonts until after base.Dispose() has
                // disposed child controls such as the clock labels.  Otherwise a
                // queued label paint can still see a dead Font instance.
            }

            base.Dispose(disposing);

            if (disposing)
            {
                try { _taskCtxGlyphFont?.Dispose(); } catch { }
                _taskCtxGlyphFont = null;

                try { _taskButtonFont?.Dispose(); } catch { }
                _taskButtonFont = null;

                try { _clockFont?.Dispose(); } catch { }
                _clockFont = null;

                try { _startMenuFont?.Dispose(); } catch { }
                _startMenuFont = null;

                try { _startSubMenuFont?.Dispose(); } catch { }
                _startSubMenuFont = null!;
            }
        }

        #endregion
    }
}
