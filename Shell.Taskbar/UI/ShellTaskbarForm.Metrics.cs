namespace Shell.Taskbar.UI
{
    public sealed partial class ShellTaskbarForm : Form
    {
        // =====================================================================
        //  METRICS: DPI-SCALED LAYOUT + FONTS + THEME APPLICATION
        // =====================================================================
        //
        // Purpose:
        // - Owns the shell’s DPI-sensitive sizing model: calculate logical/pixel
        //   metrics, build/dispose scaled fonts, and apply those values across the
        //   taskbar/start-button/task-panel/start-menu surfaces.
        //
        // Primary responsibilities here:
        // - RecalcMetrics():
        //     calculate the current layout model from the live DPI/scale context and
        //     store the resolved logical and pixel metrics used by the shell UI.
        //
        // - RebuildFonts():
        //     recreate all scaled fonts used by the taskbar and start menu, disposing
        //     old instances so repeated DPI/layout changes do not leak GDI resources.
        //
        // - ApplyLayoutMetrics():
        //     push the resolved metrics into live controls: shell padding, taskbar
        //     height, start button sizing, task button spacing, clock placement,
        //     and other layout-affecting control properties.
        //
        // Debug entry points:
        // - RecalcMetrics():
        //     first stop for “size is wrong”, “spacing feels off”, or “doesn’t match
        //     Windows-like proportions” issues.
        //
        // - RebuildFonts():
        //     first stop for “text clipped”, “text too small/large”, or “font didn’t
        //     update after DPI change” issues.
        //
        // - ApplyLayoutMetrics():
        //     best place to inspect when the metric values look correct but the live
        //     controls do not reflect them.
        //
        // Paired files while debugging:
        // - ShellTaskbarForm.cs:
        //     constructor startup order, ReapplyLayout(), and OnDpiChanged() call in here.
        // - ShellTaskbarForm.IconSizing.cs:
        //     derives taskbar/start-menu icon px from the same layout state.
        // - ShellTaskbarForm.BuildTaskbar.cs:
        //     creates controls that receive these metrics.
        // - ShellTaskbarForm.Taskbar.cs:
        //     consumes applied button sizing/padding during task refresh.
        // - ShellTaskbarForm.StartMenu.cs:
        //     consumes _startMenuFont/_startSubMenuFont and menu-related scaled values.
        //
        // Notes:
        // - Keep layout math centralized here so taskbar/start menu sizing stays
        //   consistent and Windows-like across DPI changes.
        // - Font disposal is part of the normal resize/DPI lifecycle here; this file
        //   is responsible for replacing shell-owned font instances safely.
        // - Metric recomputation should stay separate from icon-cache rebuild work;
        //   this file defines the sizes, while other files decide how to repopulate
        //   images using those sizes.
        //
        // =====================================================================

        #region Metrics / Scaling (fields)

        // ---------------- Fonts ----------------
        private Font _taskButtonFont;
        private Font _clockFont;
        private Font _startMenuFont;
        private Font _startSubMenuFont = null!;
        private Font _taskCtxGlyphFont;

        // Track the resolved pixel sizes that produced the current font objects.
        // ReapplyLayout() can run for display/work-area changes where the font
        // sizes are unchanged; in that case keep the existing Font instances so
        // live labels/buttons are not churned through needless dispose/reassign
        // cycles.
        private float _taskButtonFontSizeApplied = -1f;
        private float _clockFontSizeApplied = -1f;
        private float _startMenuFontSizeApplied = -1f;
        private float _startSubMenuFontSizeApplied = -1f;
        private float _taskCtxGlyphFontSizeApplied = -1f;

        // ---------------- Layout metrics ----------------
        private TaskbarLayoutMetrics _mDip;   // DIP source of truth
        private TaskbarLayoutMetricsPx _mPx;  // resolved PX metrics for current DPI

        #endregion

        #region Metrics / Scaling (methods)

        // Scale helpers (DIP -> PX)
        private int Scale(int dip) => (int)Math.Round(dip * (DeviceDpi / 96f));
        // Font defaults are stored as point sizes because that is how the original
        // 100% taskbar proportions were tuned. Convert them to explicit pixels
        // before creating Font instances so startup and later per-monitor DPI
        // changes follow the same single-scale path.
        private float ScaleFontPointToPx(float pointSize) => pointSize * (DeviceDpi / 72f);
        private static int MakeEven(int v) => (v & 1) == 1 ? (v - 1) : v;

        private void RecalcMetrics()
        {
            _mDip = TaskbarLayoutMetrics.Default();
            _mPx = TaskbarLayoutMetricsPx.FromDip(_mDip, Scale, ScaleFontPointToPx);

            // Keep even taskbar height for clean halves
            _mPx = _mPx.WithTaskbarHeight(MakeEven(_mPx.TaskbarHeight));

            // Apply icon-size-driven padY + derived icon px
            ApplyIconSizeMetrics();
        }

        private void ApplyLayoutMetrics()
        {
            (Font? TaskButton, Font? Clock, Font? StartMenu, Font? StartSubMenu, Font? TaskCtxGlyph) oldFonts = default;
            bool controlsApplied = false;

            SuspendLayout();
            _barLayout?.SuspendLayout();
            _taskButtons?.SuspendLayout();
            try
            {
                oldFonts = ApplyFormMetricsAndFonts();
                ApplyLayoutMetricsToControls();
                controlsApplied = true;
            }
            finally
            {
                _taskButtons?.ResumeLayout(true);
                _barLayout?.ResumeLayout(true);
                ResumeLayout(true);
            }

            // Dispose replaced fonts after the current UI message has finished.
            // That keeps replacement cleanup prompt without racing pending paint/layout
            // work that may have been queued before the new fonts were assigned.
            if (controlsApplied)
                DisposeOldLayoutFontsDeferred(oldFonts);
        }

        private (Font? TaskButton, Font? Clock, Font? StartMenu, Font? StartSubMenu, Font? TaskCtxGlyph) ApplyFormMetricsAndFonts()
        {
            Height = _mPx.TaskbarHeight;

            Font? oldTaskButtonFont;
            Font? oldClockFont;
            Font? oldStartMenuFont;
            Font? oldStartSubMenuFont;
            Font? oldTaskCtxGlyphFont;

            EnsureLayoutFont(
                ref _taskButtonFont,
                ref _taskButtonFontSizeApplied,
                "Segoe UI Semibold",
                _mPx.TaskFontSize,
                FontStyle.Regular,
                out oldTaskButtonFont);

            EnsureLayoutFont(
                ref _clockFont,
                ref _clockFontSizeApplied,
                "Segoe UI Semibold",
                _mPx.ClockFontSize,
                FontStyle.Regular,
                out oldClockFont);

            EnsureLayoutFont(
                ref _startMenuFont,
                ref _startMenuFontSizeApplied,
                "Segoe UI Semibold",
                _mPx.MenuFontSize,
                FontStyle.Regular,
                out oldStartMenuFont);

            EnsureLayoutFont(
                ref _startSubMenuFont,
                ref _startSubMenuFontSizeApplied,
                "Segoe UI Semibold",
                _mPx.SubMenuFontSize,
                FontStyle.Regular,
                out oldStartSubMenuFont);

            EnsureLayoutFont(
                ref _taskCtxGlyphFont,
                ref _taskCtxGlyphFontSizeApplied,
                "Segoe UI Symbol",
                _mPx.SubMenuFontSize,
                FontStyle.Regular,
                out oldTaskCtxGlyphFont);

            return (oldTaskButtonFont, oldClockFont, oldStartMenuFont, oldStartSubMenuFont, oldTaskCtxGlyphFont);
        }

        private static void EnsureLayoutFont(
            ref Font font,
            ref float appliedSizePx,
            string familyName,
            float requestedSizePx,
            FontStyle style,
            out Font? oldFont)
        {
            oldFont = null;

            if (font != null && AreFontSizesEquivalent(appliedSizePx, requestedSizePx))
                return;

            Font replacement = CreateUiPixelFont(familyName, requestedSizePx, style);
            oldFont = font;
            font = replacement;
            appliedSizePx = requestedSizePx;
        }

        private static bool AreFontSizesEquivalent(float appliedSizePx, float requestedSizePx)
        {
            return Math.Abs(appliedSizePx - requestedSizePx) < 0.01f;
        }

        private void DisposeOldLayoutFontsDeferred((Font? TaskButton, Font? Clock, Font? StartMenu, Font? StartSubMenu, Font? TaskCtxGlyph) oldFonts)
        {
            if (oldFonts.TaskButton == null &&
                oldFonts.Clock == null &&
                oldFonts.StartMenu == null &&
                oldFonts.StartSubMenu == null &&
                oldFonts.TaskCtxGlyph == null)
            {
                return;
            }

            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                DisposeOldLayoutFonts(oldFonts);
                return;
            }

            try
            {
                BeginInvoke(new Action(() => DisposeOldLayoutFonts(oldFonts)));
            }
            catch
            {
                DisposeOldLayoutFonts(oldFonts);
            }
        }

        private static void DisposeOldLayoutFonts((Font? TaskButton, Font? Clock, Font? StartMenu, Font? StartSubMenu, Font? TaskCtxGlyph) oldFonts)
        {
            oldFonts.TaskButton?.Dispose();
            oldFonts.Clock?.Dispose();
            oldFonts.StartMenu?.Dispose();
            oldFonts.StartSubMenu?.Dispose();
            oldFonts.TaskCtxGlyph?.Dispose();
        }

        private static Font CreateUiPixelFont(string familyName, float size, FontStyle style)
        {
            float safeSize = size > 0f ? size : 12f;

            try
            {
                return new Font(familyName, safeSize, style, GraphicsUnit.Pixel);
            }
            catch (ArgumentException)
            {
                return new Font(FontFamily.GenericSansSerif, safeSize, FontStyle.Regular, GraphicsUnit.Pixel);
            }
        }

        private void ApplyLayoutMetricsToControls()
        {
            // Root bar padding (must update on DPI/layout changes)
            if (_barLayout != null && !_barLayout.IsDisposed)
            {
                _barLayout.Padding = new Padding(_mPx.BarPadX, _mPx.BarPadY, _mPx.BarPadX, _mPx.BarPadY);
            }

            // Start button
            if (_startButton != null && !_startButton.IsDisposed)
            {
                _startButton.Width = _mPx.TaskbarHeight - (_mPx.BarPadY * 2);
                _startButton.IconBasePx = _taskbarIconPx;
                _startButton.IconTextGapPx = _mPx.IconTextGapX;
                _startButton.HopOffsetPx = _mPx.HopOffsetY;

                // These were "build-once" before — must be re-applied on DPI changes
                _startButton.Margin = new Padding(_mPx.TaskBtnGapX * 2, 0, _mPx.TaskBtnGapX * 2, 0);
                _startButton.Padding = new Padding(_mPx.TaskBtnPadX, _mPx.TaskBtnPadY, _mPx.TaskBtnPadX, _mPx.TaskBtnPadY);
            }

            if (_clockPanel != null && !_clockPanel.IsDisposed)
            {
                // keep the same "right breathing room" behavior across DPI changes
                _clockPanel.Margin = new Padding(0, 0, _mPx.TaskBtnGapX * 2, 0);

                if (_timeLabel != null && !ReferenceEquals(_timeLabel.Font, _clockFont))
                {
                    _timeLabel.Font = _clockFont;
                }

                if (_dateLabel != null && !ReferenceEquals(_dateLabel.Font, _clockFont))
                {
                    _dateLabel.Font = _clockFont;
                }

                RefreshClockMetrics();
                ApplyClockSizing();
            }

            // Task buttons
            foreach (var b in _taskBtnByHwnd.Values)
                ApplyTaskButtonMetrics(b);

            ApplyTaskContextMenuFonts();
        }

        #endregion
    }
}
