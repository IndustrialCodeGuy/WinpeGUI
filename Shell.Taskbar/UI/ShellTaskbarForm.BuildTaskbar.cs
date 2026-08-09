namespace Shell.Taskbar.UI
{
    public sealed partial class ShellTaskbarForm : Form
    {
        // =====================================================================
        //  BUILD TASKBAR UI: CONTROL CONSTRUCTION + WIRING
        // =====================================================================
        //
        // Debug entry points:
        // - BuildTaskbar()
        //     Creates the main bar layout, taskbar surface, start button,
        //     task buttons panel, and clock panel. If something is null later,
        //     it usually means it wasn’t created/wired here.
        //
        // Paired files while debugging:
        // - ShellTaskbarForm.Metrics.cs:
        //     ApplyLayoutMetricsToControls() re-applies margins/padding/fonts after DPI change.
        // - ShellTaskbarForm.Taskbar.cs:
        //     RefreshTaskButtons() and drag/reorder behavior depend on _taskButtons being wired.
        // - ShellTaskbarForm.StartMenu.cs:
        //     Start button click behavior triggers ShowStartMenu(); _startButton must exist.
        //
        // Notes:
        // - “Build” methods should not assume runtime sizes are final; after building, the
        //   constructor forces layout and then builds start menu / applies icon sizing.
        // - Clock layout is wired via SizeChanged -> ApplyClockLayout().
        //
        // =====================================================================

        #region Build Taskbar UI (fields)

        // Main surfaces
        private Panel _taskbar;
        private TableLayoutPanel _barLayout;
        private TaskButtonsPanel _taskButtons;
        private BouncyTaskbarButton _startButton;

        // Clock surfaces
        private Label _timeLabel;
        private Label _dateLabel;
        private Panel _clockPanel;

        // Cached clock text/measurement (computed on startup + DPI changes)
        private string _lastClockTimeText = "";
        private string _lastClockDateText = "";
        private int _clockWidthPx;
        private int _clockTimeHPx;
        private int _clockDateHPx;

        #endregion

        #region Build Taskbar UI (methods)

        private void BuildTaskbar()
        {
            _barLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = TaskbarTheme.ShellBack,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(_mPx.BarPadX, _mPx.BarPadY, _mPx.BarPadX, _mPx.BarPadY)
            };

            _barLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _barLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _barLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _barLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _barLayout.Paint += DrawTaskbarTopBorder;

            _barLayout.MouseDown += (s, e) =>
            {
                this.ActiveControl = null;
                ClearFocusedAppState();
            };

            _taskbar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = TaskbarTheme.ShellBack
            };

            Controls.Clear();
            Controls.Add(_taskbar);
            _taskbar.Controls.Add(_barLayout);

            _startButton = BuildStartButton();
            _barLayout.Controls.Add(_startButton, 0, 0);

            _taskButtons = BuildTaskButtonsPanel();
            _barLayout.Controls.Add(_taskButtons, 1, 0);

            _clockPanel = BuildClock();
            _barLayout.Controls.Add(_clockPanel, 2, 0);
        }

        private static void DrawTaskbarTopBorder(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control || control.ClientSize.Width <= 0)
                return;

            using SolidBrush brush = new(TaskbarTheme.TopBorder);
            e.Graphics.FillRectangle(brush, 0, 0, control.ClientSize.Width, 1);
        }

        private BouncyTaskbarButton BuildStartButton()
        {
            var btn = new BouncyTaskbarButton
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Width = _mPx.TaskbarHeight - (_mPx.BarPadY * 2),
                TabStop = true,
                TabIndex = 0,
                AutoIconModeEnabled = false,
                DisplayMode = BouncyTaskbarButton.TaskButtonDisplayMode.IconOnly,
                HopEnabled = false,
                Margin = new Padding(_mPx.TaskBtnGapX * 2, 0, _mPx.TaskBtnGapX * 2, 0)
            };

            btn.MouseDown += (s, e) =>
            {
                if (_suppressNextStartOpen)
                {
                    this.ActiveControl = null;
                    _suppressNextStartOpen = false;
                }
                else if (_startMenu?.Visible == true)
                {
                    _startMenu.Close();
                }
                else if (e.Button == MouseButtons.Left)
                {
                    ShowStartMenu();
                }
            };

            return btn;
        }

        private TaskButtonsPanel BuildTaskButtonsPanel()
        {
            var panel = new TaskButtonsPanel();

            panel.MouseDown += (s, e) =>
            {
                var child = panel.GetChildAtPoint(e.Location);
                if (child == null)
                {
                    ClearFocusedAppState();
                    this.ActiveControl = null;
                }
            };

            // Drag reorder support
            panel.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(typeof(BouncyTaskbarButton)))
                    e.Effect = DragDropEffects.Move;
            };

            panel.DragOver += (s, e) =>
            {
                if (!e.Data.GetDataPresent(typeof(BouncyTaskbarButton))) return;
                e.Effect = DragDropEffects.Move;

                var btn = e.Data.GetData(typeof(BouncyTaskbarButton)) as BouncyTaskbarButton;
                if (btn == null || btn.IsDisposed) return;

                var client = panel.PointToClient(new Point(e.X, e.Y));

                int index = panel.GetInsertIndex(client);
                int cur = panel.Controls.GetChildIndex(btn, false);

                if (index != cur && index >= 0)
                {
                    panel.SuspendLayout();
                    panel.Controls.SetChildIndex(btn, index);
                    panel.ResumeLayout(true);
                    panel.Invalidate();
                }
            };

            panel.DragDrop += (s, e) =>
            {
                _dragBtn = null;
                _dragging = false;
            };

            return panel;
        }

        private Panel BuildClock()
        {
            var clockPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, _mPx.TaskBtnGapX * 2, 0),
                Padding = new Padding(0, 0, 2, 0)
            };

            _timeLabel = new Label
            {
                AutoSize = false,
                ForeColor = TaskbarTheme.TextColor,
                Font = _clockFont,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            _dateLabel = new Label
            {
                AutoSize = false,
                ForeColor = TaskbarTheme.TextColor,
                Font = _clockFont,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            clockPanel.Controls.Add(_timeLabel);
            clockPanel.Controls.Add(_dateLabel);

            clockPanel.SizeChanged += (s, e) => ApplyClockLayout();

            UpdateClockText(refreshMetrics: false);

            // Hover chrome (panel + labels forward hover)
            void SetClockHot(bool hot)
            {
                clockPanel.BackColor = hot ? TaskbarTheme.BtnHovered : Color.Transparent;
                clockPanel.Invalidate();
            }

            clockPanel.MouseEnter += (s, e) => SetClockHot(true);
            clockPanel.MouseLeave += (s, e) => SetClockHot(false);

            _timeLabel.MouseEnter += (s, e) => SetClockHot(true);
            _timeLabel.MouseLeave += (s, e) => SetClockHot(false);
            _dateLabel.MouseEnter += (s, e) => SetClockHot(true);
            _dateLabel.MouseLeave += (s, e) => SetClockHot(false);

            return clockPanel;
        }

        private void UpdateClockText(bool refreshMetrics)
        {
            if (_timeLabel == null || _dateLabel == null)
                return;

            DateTime now = DateTime.Now;
            string timeText = now.ToString("h:mm tt");
            string dateText = now.ToString("M/d/yyyy");

            bool changed = false;

            if (!string.Equals(_lastClockTimeText, timeText, StringComparison.Ordinal))
            {
                _lastClockTimeText = timeText;

                if (!string.Equals(_timeLabel.Text, timeText, StringComparison.Ordinal))
                    _timeLabel.Text = timeText;

                changed = true;
            }

            if (!string.Equals(_lastClockDateText, dateText, StringComparison.Ordinal))
            {
                _lastClockDateText = dateText;

                if (!string.Equals(_dateLabel.Text, dateText, StringComparison.Ordinal))
                    _dateLabel.Text = dateText;

                changed = true;
            }

            if (changed && refreshMetrics)
            {
                RefreshClockMetrics();
                ApplyClockSizing();
            }
        }

        private void RefreshClockMetrics()
        {
            if (_clockFont == null) return;

            string timeS = !string.IsNullOrEmpty(_lastClockTimeText)
                ? _lastClockTimeText
                : DateTime.Now.ToString("h:mm tt");

            string dateS = !string.IsNullOrEmpty(_lastClockDateText)
                ? _lastClockDateText
                : DateTime.Now.ToString("M/d/yyyy");

            var flags = TextFormatFlags.SingleLine;

            int timeW = TextRenderer.MeasureText(timeS, _clockFont, Size.Empty, flags).Width;
            int dateW = TextRenderer.MeasureText(dateS, _clockFont, Size.Empty, flags).Width;

            int textW = Math.Max(timeW, dateW);

            // add clock panel horizontal padding (left + right)
            int padX = (_clockPanel != null && !_clockPanel.IsDisposed)
                ? _clockPanel.Padding.Right
                : 0;

            _clockWidthPx = textW + padX;

            _clockTimeHPx = TextRenderer.MeasureText(timeS, _clockFont, Size.Empty, flags).Height;
            _clockDateHPx = TextRenderer.MeasureText(dateS, _clockFont, Size.Empty, flags).Height;
        }


        private void ApplyClockSizing()
        {
            if (_clockPanel == null || _clockPanel.IsDisposed) return;

            bool widthChanged = false;

            if (_clockWidthPx > 0 && _clockPanel.Width != _clockWidthPx)
            {
                _clockPanel.Width = _clockWidthPx;
                widthChanged = true;
            }

            if (!widthChanged)
                ApplyClockLayout();
        }

        private void ApplyClockLayout()
        {
            if (_clockPanel == null || _clockPanel.IsDisposed) return;
            if (_timeLabel == null || _dateLabel == null) return;
            if (_clockFont == null) return;

            var r = _clockPanel.ClientRectangle;
            r = Rectangle.FromLTRB(
                r.Left + _clockPanel.Padding.Left,
                r.Top + _clockPanel.Padding.Top,
                r.Right - _clockPanel.Padding.Right,
                r.Bottom - _clockPanel.Padding.Bottom
            );

            if (r.Width <= 0 || r.Height <= 0) return;

            int timeH = _clockTimeHPx;
            int dateH = _clockDateHPx;

            // Safety: if layout fires before first refresh, fall back once
            if (timeH <= 0 || dateH <= 0)
            {
                var flags = TextFormatFlags.SingleLine;
                string timeS = !string.IsNullOrEmpty(_lastClockTimeText)
                    ? _lastClockTimeText
                    : DateTime.Now.ToString("h:mm tt");

                string dateS = !string.IsNullOrEmpty(_lastClockDateText)
                    ? _lastClockDateText
                    : DateTime.Now.ToString("M/d/yyyy");
                timeH = TextRenderer.MeasureText(timeS, _clockFont, Size.Empty, flags).Height;
                dateH = TextRenderer.MeasureText(dateS, _clockFont, Size.Empty, flags).Height;
            }

            int total = timeH + dateH;
            int extra = r.Height - total;

            int top = r.Top;
            if (extra > 0)
                top += extra / 2;

            // Squeeze if needed
            if (extra < 0)
            {
                timeH = Math.Max(1, (int)Math.Round(r.Height * 0.5));
                dateH = Math.Max(1, r.Height - timeH);
                top = r.Top;
            }

            _timeLabel.Bounds = new Rectangle(r.Left, top, r.Width, timeH);
            _dateLabel.Bounds = new Rectangle(r.Left, top + timeH, r.Width, dateH);
        }

        #endregion
    }
}
