using System.Drawing.Drawing2D;

namespace Shell.Taskbar.UI
{
    // =====================================================================
    //  BOUNCY TASKBAR BUTTON (owner-drawn taskbar/start button)
    // =====================================================================
    //
    // Purpose:
    // - Single control used for:
    //     - Start button (icon-only, no hop)
    //     - Task buttons (icon+label or icon-only auto mode)
    // - Owns its painting + lightweight visuals (press shrink + optional hop animation)
    // - Avoids WinForms/theming “helpful” hover/down painting and focus cues.
    //
    // Where it’s driven from (post Level 1 split):
    // - UI/ShellTaskbarForm.BuildTaskbar.cs:
    //     - Creates the Start button and task button instances.
    // - UI/ShellTaskbarForm.Metrics.cs:
    //     - Applies fonts, padding, VisualOuterPadX, and other DPI-driven metrics.
    // - UI/ShellTaskbarForm.Taskbar.cs:
    //     - Sets IconBasePx (taskbar icon size) and DisplayMode policy.
    //     - Calls TryUpdateDisplayedTitle(...) based on TextAvailablePx.
    //     - Calls ApplyFocusState(...) based on foreground window.
    // - UI/ShellTaskbarForm.IconSizing.cs:
    //     - Changing icon-size setting alters padding (TaskBtnPadY) which impacts
    //       the internal content rectangle and icon size decisions.
    //
    // Painting / state rules (important for debugging “fuzzy icons”):
    // - Background strategy:
    //     - Always clears full control with TaskbarTheme.BtnDefault.
    //     - Non-default fills (Focused / Hover / Pressed) are drawn ONLY inside
    //       the inner "chrome" rect (ClientRect +/- VisualOuterPadX).
    //
    // - Text stability strategy:
    //     - Text drawing never uses hop/press transforms.
    //     - Text position is based on base iconPx + gap only (no animation influence).
    //     - Goal: prevent label jitter while the icon animates.
    //
    // - Icon transform strategy:
    //     - Only the icon draw is transformed (Translate/Scale around icon center),
    //       and hop is snapped to whole pixels to reduce half-pixel resampling.
    //
    // - Rendering strategy:
    //     - Uses DrawImageUnscaled when the bitmap already matches iconPx.
    //     - Uses DrawImage (scaled) only when needed (or during animation).
    //
    // Layout / sizing inputs (what must be set by ShellTaskbarForm):
    // - Padding: applied per DPI + icon-size setting.
    // - VisualOuterPadX: used to create "chrome" inset and preserve visual gaps.
    // - IconBasePx: expected to be set by ShellTaskbarForm to the resolved taskbar icon px.
    //   If not set, it falls back to Image.Width.
    //
    // Text truncation model:
    // - TryUpdateDisplayedTitle(fullTitle):
    //     - Computes TextAvailablePx (based on Width, Padding, VisualOuterPadX, iconPx, gap).
    //     - AutoIconModeEnabled:
    //         - If too narrow, switches to IconOnly.
    //         - If wide enough again, restores Label mode.
    //     - Uses binary search truncation with ellipsis for stable performance.
    //
    // Animation model:
    // - Press visual:
    //     - OnMouseDown: snaps icon scale to PressMinScale.
    //     - OnMouseUp: snaps icon scale back to 1.0.
    //     - Left and right clicks both use the same press visual.
    // - Hop (optional):
    //     - HopUp/HopDown request a sine-wave displacement, returns to 0.
    //     - Intended to be triggered by ShellTaskbarForm on minimize/restore events.
    // - Drag visual:
    //     - BeginDragVisual/EndDragVisual used by reorder logic to slightly enlarge
    //       and to clear "pressed" state during drag.
    //
    // Cleanup:
    // - Owns one WinForms timer (_hopTimer) and disposes it.
    // - Do not share these timers; instances are per button.
    //
    // Debug tips:
    // - “Icon looks softer/fuzzier on hover”:
    //     Check whether hover overlay is being drawn atop focused state,
    //     and whether the icon is being scaled (DrawImage path) vs unscaled.
    // - “Text jitters while pressing”:
    //     Text is intentionally not transformed; verify no external code is resizing the control.
    // - “Buttons flip to icon-only too early/late”:
    //     Verify IconBasePx + padding are updated on DPI/layout changes,
    //     and that TextAvailablePx is computed after sizing/layout settles.
    // =====================================================================

    internal sealed class BouncyTaskbarButton : Button
    {
        // =====================================================================
        // Basics
        // =====================================================================

        protected override bool ShowFocusCues => false;

        public bool AutoIconModeEnabled { get; set; } = true;

        // Do not store taskbar icons in ButtonBase.Image. ButtonBase watches
        // Image for animation internally, and if a cached image is detached and
        // disposed while the taskbar is repainting it can throw from
        // ImageAnimator.CanAnimate(). This owner-drawn control keeps the live
        // image in its own field and leaves the base Image property null.
        private Image? _buttonImage;

        public new Image? Image
        {
            get => _buttonImage;
            set
            {
                if (ReferenceEquals(_buttonImage, value))
                    return;

                _buttonImage = value;
                base.Image = null;
                Invalidate();
            }
        }

        private Image? _pressedImage;

        public Image? PressedImage
        {
            get => _pressedImage;
            set
            {
                if (ReferenceEquals(_pressedImage, value))
                    return;

                _pressedImage = value;
                Invalidate();
            }
        }

        private int _visualOuterPadX;
        public int VisualOuterPadX
        {
            get => _visualOuterPadX;
            set
            {
                int v = Math.Max(0, value);
                if (_visualOuterPadX == v) return;
                _visualOuterPadX = v;
                Invalidate();
            }
        }

        // =====================================================================
        // Layout (DPI-aware)
        // =====================================================================

        private int _iconTextGapPx;
        public int IconTextGapPx
        {
            get => _iconTextGapPx;
            set
            {
                int v = Math.Max(0, value);
                if (_iconTextGapPx == v) return;
                _iconTextGapPx = v;
                Invalidate();
            }
        }

        private int _iconBasePx;
        public int IconBasePx
        {
            get => _iconBasePx;
            set
            {
                int v = Math.Max(0, value);
                if (_iconBasePx == v) return;
                _iconBasePx = v;
                Invalidate();
            }
        }

        // =====================================================================
        // Visual: Press (shrink/restore)
        // =====================================================================

        // How far to shrink on mouse down
        private const float PressMinScale = 0.80f;

        // Current scale applied to the icon
        private float _pressScale = 1f;

        // =====================================================================
        // Animation: Hop
        // =====================================================================

        private float _hopYPx = 0f;

        private int _hopOffsetPx;
        public int HopOffsetPx
        {
            get => _hopOffsetPx;
            set => _hopOffsetPx = Math.Max(0, value);
        }

        private float _hopTo = 0f;
        private int _hopFrame;
        private const int HopFrames = 20;
        private const int HopIntervalMs = 15;
        public bool HopEnabled { get; set; } = true;

        private readonly System.Windows.Forms.Timer _hopTimer = new();

        // =====================================================================
        // Drag visual (move state)
        // =====================================================================

        private bool _dragVisual;
        private const float DragScale = 1.12f;

        // =====================================================================
        // Visual state
        // =====================================================================

        private bool _hover;
        private bool _pressed;
        private bool _pressedVisualLocked;
        private bool _suppressNextPressAnimation;
        private bool _isFocused;

        // =====================================================================
        // Public API
        // =====================================================================
        public enum TaskButtonDisplayMode { Label, IconOnly }

        private TaskButtonDisplayMode _displayMode = TaskButtonDisplayMode.Label;
        public TaskButtonDisplayMode DisplayMode
        {
            get => _displayMode;
            set
            {
                if (_displayMode == value) return;
                _displayMode = value;

                if (_displayMode == TaskButtonDisplayMode.IconOnly)
                {
                    Text = "";
                    ImageAlign = ContentAlignment.MiddleCenter;
                }
                else
                {
                    ImageAlign = ContentAlignment.MiddleLeft;
                }

                TextImageRelation = TextImageRelation.Overlay;
                Invalidate();
            }
        }

        public void SetPressedVisualLocked(bool locked)
        {
            if (_pressedVisualLocked == locked)
                return;

            _pressedVisualLocked = locked;

            if (locked)
            {
                _pressed = true;
                _pressScale = PressMinScale;
                Invalidate();
                return;
            }

            ResetPressVisual();
        }

        public void SuppressNextPressAnimation()
        {
            _suppressNextPressAnimation = true;

            if (_pressedVisualLocked)
                return;

            ResetPressVisual();
        }

        private void ResetPressVisual()
        {
            _pressed = false;
            _pressScale = 1f;
            Invalidate();
        }


        public void ApplyFocusState(bool focused)
        {
            if (_isFocused == focused) return;
            _isFocused = focused;
            Invalidate();
        }

        // Call these later from “source of truth” window events if desired.
        public void HopUp()
        {
            if (!HopEnabled) return;
            StartHop(to: -HopOffsetPx);
        }

        public void HopDown()
        {
            if (!HopEnabled) return;
            StartHop(to: +HopOffsetPx);
        }

        public void BeginDragVisual()
        {
            _dragVisual = true;

            // Clear the pressed overlay before entering drag visual.
            _pressed = false;

            // Slightly larger while in move state
            _pressScale = DragScale;

            Invalidate();
        }

        public void EndDragVisual()
        {
            _dragVisual = false;

            // Return to normal
            _pressScale = 1f;

            Invalidate();
        }

        // =====================================================================
        // Text measurement / truncation (owned by the button)
        // =====================================================================

        private const string MinReadableLabel = "WW…";
        public int MinReadableLabelPx => MeasureTextWidth(MinReadableLabel, Font);
        private const TextFormatFlags PaintTextFlags =
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix;

        public int MinLabelButtonWidthPx
        {
            get
            {
                int iconPx = IconBasePx > 0 ? IconBasePx : (Image?.Width ?? 0);
                bool hasIcon = (Image != null) && (iconPx > 0);

                int w = (VisualOuterPadX * 2) + Padding.Left + Padding.Right;

                if (hasIcon)
                    w += iconPx + IconTextGapPx;

                w += MinReadableLabelPx;
                return w;
            }
        }

        public int TextAvailablePx
        {
            get
            {
                if (_displayMode == TaskButtonDisplayMode.IconOnly)
                    return 0;

                int w = Width - (VisualOuterPadX * 2) - Padding.Left - Padding.Right;
                if (w <= 0) return 0;

                int iconPx = IconBasePx > 0 ? IconBasePx : (Image?.Width ?? 0);
                bool hasIcon = Image != null && iconPx > 0;

                if (hasIcon)
                    w -= (iconPx + IconTextGapPx);

                return Math.Max(0, w);
            }
        }

        public bool TryUpdateDisplayedTitle(string? fullTitle)
        {
            fullTitle ??= "";

            int maxPx = TextAvailablePx;

            // AUTO ICON MODE (only if enabled)
            if (AutoIconModeEnabled)
            {
                int minLabelPx = MinReadableLabelPx;

                if (maxPx < minLabelPx)
                {
                    bool changed = false;

                    if (_displayMode != TaskButtonDisplayMode.IconOnly)
                    {
                        DisplayMode = TaskButtonDisplayMode.IconOnly;
                        changed = true;
                    }
                    else if (!string.IsNullOrEmpty(Text))
                    {
                        Text = "";
                        changed = true;
                    }

                    if (changed) Invalidate();
                    return changed;
                }

                // Enough space again → restore Label
                if (_displayMode != TaskButtonDisplayMode.Label)
                    DisplayMode = TaskButtonDisplayMode.Label;
            }

            // ---- Manual OR Auto-Label path continues here ----

            if (_displayMode == TaskButtonDisplayMode.IconOnly)
                return false;

            string next =
                (string.IsNullOrEmpty(fullTitle) || maxPx <= 0)
                    ? ""
                    : TruncToWidth(fullTitle, Font, maxPx);

            if (string.Equals(Text, next, StringComparison.Ordinal))
                return false;

            Text = next;
            Invalidate();
            return true;
        }

        // ---- keep these private inside the button ----

        private const TextFormatFlags TaskTextFlags =
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        private static int MeasureTextWidth(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            // safety for glyph overhang / DPI rounding (especially Bold)
            const int bleedPx = 8;

            return TextRenderer.MeasureText(
                text,
                font,
                Size.Empty,
                TaskTextFlags
            ).Width + bleedPx;
        }

        private static string TruncToWidth(string text, Font font, int maxWidthPx)
        {
            if (string.IsNullOrEmpty(text)) return "";

            if (MeasureTextWidth(text, font) <= maxWidthPx)
                return text;

            const string ellipsis = "…";
            int lo = 0, hi = text.Length;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                string s = text.Substring(0, mid) + ellipsis;

                if (MeasureTextWidth(s, font) <= maxWidthPx)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return text.Substring(0, Math.Max(0, lo - 1)) + ellipsis;
        }

        // =====================================================================
        // Ctor / Dispose
        // =====================================================================

        public BouncyTaskbarButton()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            // ---- invariant colors (never change for focus/hover/press) ----
            BackColor = TaskbarTheme.BtnDefault;     // or TaskbarTheme.ShellBack if those are identical
            ForeColor = TaskbarTheme.TextColor;

            // ---- invariant chrome / framework suppression ----
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            base.Image = null;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.BorderColor = BackColor;

            // prevent WinForms/theming from injecting full-bleed hover/down fills
            FlatAppearance.MouseOverBackColor = TaskbarTheme.BtnDefault;
            FlatAppearance.MouseDownBackColor = TaskbarTheme.BtnDefault;

            _hopTimer.Interval = HopIntervalMs;
            _hopTimer.Tick += (s, e) => StepHop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _hopTimer.Stop(); } catch { }
                try { _hopTimer.Dispose(); } catch { }

                _buttonImage = null;
                _pressedImage = null;
            }
            base.Dispose(disposing);
        }

        // =====================================================================
        // Mouse handling: press shrink/expand + drag capture hygiene
        // =====================================================================

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;

            if (!_pressedVisualLocked &&
                (Control.MouseButtons & (MouseButtons.Left | MouseButtons.Right)) == 0)
            {
                ResetPressVisual();
            }
            else
            {
                Invalidate();
            }

            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                if (_suppressNextPressAnimation)
                {
                    _suppressNextPressAnimation = false;
                    ResetPressVisual();
                }
                else if (!_pressedVisualLocked)
                {
                    _pressed = true;

                    _pressScale = PressMinScale;

                    Invalidate();
                }
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                if (_suppressNextPressAnimation)
                {
                    _suppressNextPressAnimation = false;
                    ResetPressVisual();
                }
                else if (!_pressedVisualLocked)
                {
                    _pressed = false;

                    _pressScale = 1f;

                    Invalidate();
                }
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);

            // If we lost capture and we're not dragging, don't stay "stuck pressed"
            if (!_pressedVisualLocked && !_dragVisual &&
                (_pressed || _pressScale != 1f) &&
                (Control.MouseButtons & (MouseButtons.Left | MouseButtons.Right)) == 0)
            {
                ResetPressVisual();
            }
        }

        // =====================================================================
        // Hop animation
        // =====================================================================

        private void StartHop(float to)
        {
            // Interrupt any hop in progress
            _hopTimer.Stop();
            _hopYPx = 0f;

            // Reset and start a fresh hop
            _hopTo = to;
            _hopFrame = 0;

            _hopTimer.Start();
            Invalidate();
        }

        private void StepHop()
        {
            _hopFrame++;

            float t = _hopFrame / (float)HopFrames;
            if (t >= 1f) t = 1f;

            // Up-then-back using sin(pi*t): 0 -> 1 -> 0
            float wave = (float)System.Math.Sin(t * System.Math.PI);

            // Peak displacement based on _hopTo (sign controls direction)
            _hopYPx = _hopTo * wave;

            if (_hopFrame >= HopFrames)
            {
                _hopTimer.Stop();
                _hopYPx = 0f;
            }

            Invalidate();
        }

        // =====================================================================
        // Paint
        // =====================================================================
        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Do nothing. We'll paint everything in OnPaint.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            // Always paint the full button with default (never state colors)
            g.Clear(TaskbarTheme.BtnDefault);

            // Inner chrome rect: this is where non-default states are allowed
            var chrome = Rectangle.FromLTRB(
                ClientRectangle.Left + VisualOuterPadX,
                ClientRectangle.Top,
                ClientRectangle.Right - VisualOuterPadX,
                ClientRectangle.Bottom
            );

            if (chrome.Width <= 0 || chrome.Height <= 0)
                return;

            // Focus fill (solid) ONLY inside chrome
            if (_isFocused)
            {
                using var fb = new SolidBrush(TaskbarTheme.BtnFocused);
                g.FillRectangle(fb, chrome);
            }

            // Hover/press overlays ONLY inside chrome
            // (and if you want: don't overlay hover when focused)
            if (_pressed)
            {
                using var ov = new SolidBrush(TaskbarTheme.BtnPressed);
                g.FillRectangle(ov, chrome);
            }
            else if (_hover && !_isFocused)
            {
                using var ov = new SolidBrush(TaskbarTheme.BtnHovered);
                g.FillRectangle(ov, chrome);
            }

            // Now treat content area as INSIDE chrome, minus Padding
            var r = Rectangle.FromLTRB(
                chrome.Left + Padding.Left,
                chrome.Top + Padding.Top,
                chrome.Right - Padding.Right,
                chrome.Bottom - Padding.Bottom
            );

            if (r.Width <= 0 || r.Height <= 0)
                return;

            // Stable layout origin (text never depends on any animated position)
            int layoutX = r.Left;

            bool drawText = (_displayMode == TaskButtonDisplayMode.Label) && !string.IsNullOrEmpty(Text);

            // ---------------- Icon base size (DPI-correct) ----------------
            // ShellTaskbarForm should set IconBasePx from GetTaskbarIconPxFromLayout().
            // Fallback keeps old behavior if not set.
            bool usePressedImage = PressedImage != null && _pressScale < 0.999f;
            Image? imageToDraw = usePressedImage ? PressedImage : Image;

            int iconPx = IconBasePx > 0 ? IconBasePx : (imageToDraw?.Width ?? 0);
            iconPx = Math.Max(0, Math.Min(iconPx, 64));

            // Decide if we actually have an icon
            bool hasIcon = (imageToDraw != null) && (iconPx > 0);

            int gap = (hasIcon && drawText) ? IconTextGapPx : 0;

            // Icon base rect (this is the anchor for text too)
            int iconX = r.Left;
            if (_displayMode == TaskButtonDisplayMode.IconOnly)
            {
                // center icon when icon-only (removes the "phantom gap" feel)
                iconX = r.Left + ((r.Width - iconPx) / 2);
            }

            int iconY = r.Top + ((r.Height - iconPx) / 2);
            float iconCx = iconX + (iconPx / 2f);
            float iconCy = iconY + (iconPx / 2f);

            // ---------------- Draw ICON (icon-only transform) ----------------
            if (hasIcon)
            {
                bool iconAnimating = (_pressScale != 1f) || (_hopYPx != 0f);

                // Snap anchor + hop to whole pixels (helps avoid half-pixel resampling)
                float ax = (float)Math.Round(iconCx);
                float ay = (float)Math.Round(iconCy);
                float hop = (float)Math.Round(_hopYPx);

                // Save only what we touch for icon drawing
                var state = g.Save();
                try
                {
                    // Use high quality only for the icon draw.
                    //g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    //g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.None;
                    g.CompositingQuality = CompositingQuality.GammaCorrected;
                    g.CompositingMode = CompositingMode.SourceOver;

                    if (iconAnimating)
                    {
                        // Apply transform ONLY around icon center
                        g.TranslateTransform(ax, ay + hop);
                        g.ScaleTransform(_pressScale, _pressScale);
                        g.TranslateTransform(-ax, -ay);
                    }

                    if (imageToDraw != null)
                    {
                        // Always draw into an explicit device-pixel rectangle.
                        // DrawImageUnscaled can honor bitmap DPI differently on a high-DPI
                        // startup surface, which makes 96-DPI cached bitmaps appear oversized.
                        // The pressed path already used DrawImage(rect), which is why the
                        // shrunken/pressed icon looked closer to the intended size.
                        if (!iconAnimating && imageToDraw.Width == iconPx && imageToDraw.Height == iconPx)
                        {
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.None;
                        }
                        else
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        }

                        g.DrawImage(imageToDraw, new Rectangle(iconX, iconY, iconPx, iconPx));
                    }
                }
                finally
                {
                    g.Restore(state);
                }
            }

            // ---------------- Draw TEXT (no scale/hop) ----------------
            if (drawText)
            {
                // Text left edge is based on base icon width, not animated width => never shifts.
                int textLeft = layoutX + (hasIcon ? iconPx : 0) + gap;

                var textRect = Rectangle.FromLTRB(
                    textLeft,
                    r.Top,
                    r.Right,
                    r.Bottom
                );

                TextRenderer.DrawText(g, Text, Font, textRect, ForeColor, PaintTextFlags);
            }
        }
    }
}
