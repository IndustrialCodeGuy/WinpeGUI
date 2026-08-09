namespace Shell.Taskbar.UI
{
    /// <summary>
    /// Pixel-resolved layout metrics for the current DPI.
    ///
    /// Intent:
    /// - Derived from TaskbarLayoutMetrics (100% reference).
    /// - Normal layout values scale from DIP -> PX through one central path.
    /// - Values represent ONE SIDE (no implicit divide-by-2).
    /// </summary>
    internal readonly struct TaskbarLayoutMetricsPx
    {
        public readonly int TaskbarHeight;

        // Pixel values for current DPI (ONE SIDE)
        public readonly int BarPadX;
        public readonly int BarPadY;
        public readonly int TaskBtnPadX;
        public readonly int TaskBtnPadY;
        public readonly int TaskBtnGapX;
        public readonly int IconTextGapX;
        public readonly int HopOffsetY;

        public readonly int TaskBtnMaxW;

        // Font sizes are pixel-resolved values converted from the 100% point-size
        // defaults. Font instances should be created with GraphicsUnit.Pixel so
        // these values are not DPI-scaled a second time by GDI.
        public readonly float TaskFontSize;
        public readonly float ClockFontSize;
        public readonly float MenuFontSize;
        public readonly float SubMenuFontSize;

        private TaskbarLayoutMetricsPx(
            int taskbarHeight,
            int barPadX,
            int barPadY,
            int taskBtnPadX,
            int taskBtnPadY,
            int taskBtnMaxW,
            int taskBtnGapX,
            int iconTextGapX,
            int hopOffsetY,
            float taskFontSize,
            float clockFontSize,
            float menuFontSize,
            float subMenuFontSize)
        {
            TaskbarHeight = taskbarHeight;
            BarPadX = barPadX;
            BarPadY = barPadY;
            TaskBtnPadX = taskBtnPadX;
            TaskBtnPadY = taskBtnPadY;
            TaskBtnMaxW = taskBtnMaxW;
            TaskBtnGapX = taskBtnGapX;
            IconTextGapX = iconTextGapX;
            HopOffsetY = hopOffsetY;
            TaskFontSize = taskFontSize;
            ClockFontSize = clockFontSize;
            MenuFontSize = menuFontSize;
            SubMenuFontSize = subMenuFontSize;
        }

        public static TaskbarLayoutMetricsPx FromDip(
            TaskbarLayoutMetrics d,
            Func<int, int> scale,
            Func<float, float> scaleF)
        {
            return new TaskbarLayoutMetricsPx(
                taskbarHeight: scale(d.TaskbarHeight),
                barPadX: scale(d.BarPadX),
                barPadY: scale(d.BarPadY),
                taskBtnPadX: scale(d.TaskBtnPadX),
                taskBtnPadY: scale(d.TaskBtnPadY),
                taskBtnMaxW: scale(d.TaskBtnMaxW),
                taskBtnGapX: scale(d.TaskBtnGapX),
                iconTextGapX: scale(d.IconTextGapX),
                hopOffsetY: scale(d.HopOffsetY),
                taskFontSize: scaleF(d.TaskFontSize),
                clockFontSize: scaleF(d.ClockFontSize),
                menuFontSize: scaleF(d.MenuFontSize),
                subMenuFontSize: scaleF(d.SubMenuFontSize)
            );
        }

        public TaskbarLayoutMetricsPx WithTaskbarHeight(int taskbarHeight)
        {
            return new TaskbarLayoutMetricsPx(
                taskbarHeight: taskbarHeight,
                barPadX: BarPadX,
                barPadY: BarPadY,
                taskBtnPadX: TaskBtnPadX,
                taskBtnPadY: TaskBtnPadY,
                taskBtnMaxW: TaskBtnMaxW,
                taskBtnGapX: TaskBtnGapX,
                iconTextGapX: IconTextGapX,
                hopOffsetY: HopOffsetY,
                taskFontSize: TaskFontSize,
                clockFontSize: ClockFontSize,
                menuFontSize: MenuFontSize,
                subMenuFontSize: SubMenuFontSize
            );
        }

        public TaskbarLayoutMetricsPx WithTaskBtnPadY(int taskBtnPadY)
        {
            return new TaskbarLayoutMetricsPx(
                taskbarHeight: TaskbarHeight,
                barPadX: BarPadX,
                barPadY: BarPadY,
                taskBtnPadX: TaskBtnPadX,
                taskBtnPadY: taskBtnPadY,
                taskBtnMaxW: TaskBtnMaxW,
                taskBtnGapX: TaskBtnGapX,
                iconTextGapX: IconTextGapX,
                hopOffsetY: HopOffsetY,
                taskFontSize: TaskFontSize,
                clockFontSize: ClockFontSize,
                menuFontSize: MenuFontSize,
                subMenuFontSize: SubMenuFontSize
            );
        }
    }
}
