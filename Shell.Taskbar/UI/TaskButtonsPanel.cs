namespace Shell.Taskbar.UI
{
    internal sealed class TaskButtonsPanel : Panel
    {
        // Source of truth is TaskbarLayoutMetricsPx; this is just the applied runtime value.
        private int _iconPx;
        public int IconPx
        {
            get => _iconPx;
            set
            {
                int v = Math.Max(0, value);
                if (_iconPx == v) return;
                _iconPx = v;
                PerformLayout();
            }
        }

        private int _innerPadX;
        public int InnerPadX
        {
            get => _innerPadX;
            set
            {
                int v = Math.Max(0, value);
                if (_innerPadX == v) return;
                _innerPadX = v;
                PerformLayout();
            }
        }

        private int _visualGapX;
        public int VisualGapX
        {
            get => _visualGapX;
            set
            {
                int v = Math.Max(0, value);
                if (_visualGapX == v) return;
                _visualGapX = v;
                PerformLayout();
            }
        }


        public void SetMetrics(int iconPx, int innerPadX, int visualGapX)
        {
            iconPx = Math.Max(0, iconPx);
            innerPadX = Math.Max(0, innerPadX);
            visualGapX = Math.Max(0, visualGapX);

            if (_iconPx == iconPx &&
                _innerPadX == innerPadX &&
                _visualGapX == visualGapX)
            {
                return;
            }

            _iconPx = iconPx;
            _innerPadX = innerPadX;
            _visualGapX = visualGapX;

            PerformLayout();
        }

        public int MinButtonWidth =>
            Math.Max(0, IconPx)
            + (Math.Max(0, InnerPadX) * 2)
            + (Math.Max(0, VisualGapX) * 2);

        public TaskButtonsPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            Dock = DockStyle.Fill;
            BackColor = Color.Transparent;
            Margin = new Padding(0);
            Padding = new Padding(0);
            AutoScroll = false;
            AllowDrop = true;
        }

        public int GetInsertIndex(Point clientPoint)
        {
            if (Controls.Count == 0) return 0;

            for (int i = 0; i < Controls.Count; i++)
            {
                var c = Controls[i];
                if (!c.Visible) continue;

                int midX = c.Left + (c.Width / 2);
                if (clientPoint.X < midX)
                    return i;
            }

            return Controls.Count;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            int h = ClientSize.Height;
            if (h < 0) h = 0;

            // Keep even height invariant
            if ((h & 1) == 1) h -= 1;

            int x = 0;

            for (int i = 0; i < Controls.Count; i++)
            {
                var c = Controls[i];
                if (!c.Visible) continue;

                int w = c.Width;
                if (MinButtonWidth > 0)
                    w = Math.Max(MinButtonWidth, w);

                // No gaps: tile edge-to-edge
                var next = new Rectangle(x, 0, w, h);
                if (c.Bounds != next)
                    c.Bounds = next;

                x += w;
            }
        }

    }
}
