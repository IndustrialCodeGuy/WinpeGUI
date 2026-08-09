namespace Shell.Taskbar.UI
{
    /// <summary>
    /// Layout defaults expressed in DIPs, using 100% (96 DPI) as the reference.
    ///
    /// Intent:
    /// - These are the defaults at 100%.
    /// - Values represent ONE SIDE (e.g., left padding, right padding) — do not divide by 2.
    /// - If a call site needs "both sides", it should multiply by 2 explicitly.
    /// - Runtime pixels are resolved centrally by TaskbarLayoutMetricsPx.FromDip().
    /// </summary>
    internal readonly struct TaskbarLayoutMetrics
    {
        // ---------- Taskbar (DIP @ 100%) ----------
        public readonly int TaskbarHeight;
        public readonly int TaskBtnMaxW;

        // Fonts are specified as point-size defaults at 100%. They are converted
        // to explicit pixels in TaskbarLayoutMetricsPx so startup and DPI-change
        // font sizing use the same single-scale path.
        public readonly float TaskFontSize;
        public readonly float ClockFontSize;
        public readonly float MenuFontSize;
        public readonly float SubMenuFontSize;

        // ---------- Chrome / Padding (DIP @ 100%, ONE SIDE) ----------
        public readonly int BarPadX;
        public readonly int BarPadY;
        public readonly int TaskBtnPadX;
        public readonly int TaskBtnPadY;
        public readonly int TaskBtnGapX;
        public readonly int IconTextGapX;
        public readonly int HopOffsetY;

        private TaskbarLayoutMetrics(
            int taskbarHeight,
            int taskBtnMaxW,
            float taskFontSize,
            float clockFontSize,
            float menuFontSize,
            float subMenuFontSize,
            int barPadX,
            int barPadY,
            int taskBtnPadX,
            int taskBtnPadY,
            int taskBtnGapX,
            int iconTextGapX,
            int hopOffsetY)
        {
            TaskbarHeight = taskbarHeight;
            TaskBtnMaxW = taskBtnMaxW;
            TaskFontSize = taskFontSize;
            ClockFontSize = clockFontSize;
            MenuFontSize = menuFontSize;
            SubMenuFontSize = subMenuFontSize;
            BarPadX = barPadX;
            BarPadY = barPadY;
            TaskBtnPadX = taskBtnPadX;
            TaskBtnPadY = taskBtnPadY;
            TaskBtnGapX = taskBtnGapX;
            IconTextGapX = iconTextGapX;
            HopOffsetY = hopOffsetY;
        }

        // Single layout (formerly "Medium")
        public static TaskbarLayoutMetrics Default()
        {
            return new TaskbarLayoutMetrics(
                taskbarHeight: 48,
                taskBtnMaxW: 196,
                taskFontSize: 9.5f,
                clockFontSize: 8.5f,
                menuFontSize: 9.5f,
                subMenuFontSize: 9.0f,
                barPadX: 10,
                barPadY: 2,
                taskBtnPadX: 5,
                taskBtnPadY: 8,
                taskBtnGapX: 1,
                iconTextGapX: 4,
                hopOffsetY: 4
            );
        }
    }
}
