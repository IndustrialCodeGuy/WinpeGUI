namespace Shell.Taskbar.UI
{
    public sealed partial class ShellTaskbarForm : Form
    {
        // =====================================================================
        //  ICON SIZING: TASKBAR / START-MENU ICON PX POLICY
        // =====================================================================
        //
        // Purpose:
        // - Owns icon-size policy for the shell: resolve the effective taskbar icon
        //   size, derive related start-menu/submenu icon sizes, and expose a single
        //   Windows-like sizing model to the rest of the shell.
        //
        // Primary responsibilities here:
        // - ResolveTaskbarIconPx():
        //     choose the effective taskbar icon pixel size from the current layout
        //     metrics and any shell icon-size setting/override policy.
        //
        // - ResolveStartMenuIconPx():
        //     derive the main start-menu item icon size so it remains visually in
        //     proportion with the current taskbar and menu text metrics.
        //
        // - ResolveSubMenuIconPx():
        //     derive submenu icon size for Logoff / power / other nested menu items.
        //
        // - Expose current icon sizes to:
        //     taskbar button refresh, TaskWindowEnumerator icon extraction,
        //     start-menu image assignment, and any DPI-triggered rebuild path.
        //
        // Debug entry points:
        // - ResolveTaskbarIconPx():
        //     first stop for “taskbar icons are too big/small”, “don’t match the
        //     button height”, or “don’t change when DPI/layout changes”.
        //
        // - ResolveStartMenuIconPx() / ResolveSubMenuIconPx():
        //     first stop for start-menu image proportion issues.
        //
        // Paired files while debugging:
        // - ShellTaskbarForm.Metrics.cs:
        //     supplies the underlying taskbar/menu height and spacing metrics used
        //     to choose icon sizes.
        // - ShellTaskbarForm.cs:
        //     ReapplyLayout() consumes these values when reapplying live UI state.
        // - ShellTaskbarForm.Taskbar.cs:
        //     applies taskbar icon px to TaskWindowEnumerator and live task buttons.
        // - ShellTaskbarForm.StartMenu.cs:
        //     assigns root/submenu ToolStrip item images using the resolved sizes.
        //
        // Notes:
        // - This file should stay policy-only: it decides icon px, but should avoid
        //   directly rebuilding controls or icon caches.
        // - Keeping taskbar and start-menu icon sizing in one place helps preserve a
        //   coherent Windows-like visual rhythm after DPI or layout changes.
        // - If icon proportions look wrong, check the metric inputs first, then the
        //   live rebuild/application path in Taskbar or StartMenu.
        //
        // =====================================================================

        #region Icons (fields)

        private enum IconSizeSetting { Small, Medium, Large }

        // default
        private IconSizeSetting _iconSizeSetting = IconSizeSetting.Small;

        // cached derived icon px (computed only when needed)
        private int _smallIconPx = -1;
        private int _largeIconPx = -1;

        // Task button mode control (form-level policy)
        private BouncyTaskbarButton.TaskButtonDisplayMode _taskBtnMode =
            BouncyTaskbarButton.TaskButtonDisplayMode.Label;

        private bool _forceIconOnly = false;
        private bool? _forceIconOnlyApplied = null;

        #endregion

        #region Icons (methods)

        // IconSizeSetting: smaller icons == more vertical padding (DIP @ 100%).
        private int GetTaskBtnPadYDipForIconSize(IconSizeSetting s) => s switch
        {
            IconSizeSetting.Small => 8,
            IconSizeSetting.Medium => 7,
            IconSizeSetting.Large => 6,
            _ => 8
        };

        private static int SnapTaskbarIconPx(int requestedPx)
        {
            // Prefer common Windows icon resource sizes. Avoid odd sizes like 30/36
            // because they often force resampling and look soft in the taskbar.
            if (requestedPx <= 16) return 16;
            if (requestedPx <= 22) return 20; 
            if (requestedPx <= 28) return 24;
            if (requestedPx <= 36) return 32;
            if (requestedPx <= 44) return 40;
            return 48;
        }

        private static int SnapStartMenuIconPx(int requestedPx)
        {
            if (requestedPx <= 16) return 16;
            if (requestedPx <= 22) return 20;
            if (requestedPx <= 28) return 24;
            if (requestedPx <= 36) return 32;
            if (requestedPx <= 44) return 40;
            if (requestedPx <= 56) return 48;
            return 64;
        }

        private void RefreshIconPxMetrics()
        {
            _smallIconPx = SnapTaskbarIconPx(Scale(24));
            _largeIconPx = SnapStartMenuIconPx(Scale(34));
        }

        private void ApplyIconSizeMetrics()
        {
            // IconSize impacts TaskBtnPadY and derived icon sizes through the same DPI path.
            _mPx = _mPx.WithTaskBtnPadY(Scale(GetTaskBtnPadYDipForIconSize(_iconSizeSetting)));
            RefreshIconPxMetrics();
        }

        private int GetLargeIconPxFromLayout() => _largeIconPx > 0 ? _largeIconPx : 34;
        private int GetSmallIconPxFromLayout() => _smallIconPx > 0 ? _smallIconPx : 28;

        #endregion
    }
}
