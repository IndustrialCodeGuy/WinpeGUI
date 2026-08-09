using Shared.Shell.Theming;

namespace Shell.Taskbar.UI
{
    // =====================================================================
    //  THEME: TASKBAR COLOR ACCESSORS
    // =====================================================================
    //
    // Purpose:
    // - Compatibility wrapper around Shared.Shell.Theming.ShellTheme.
    // - Keeps existing taskbar code unchanged while allowing Explorer.UI to use
    //   the same palette without referencing the taskbar project.
    // =====================================================================

    internal static class TaskbarTheme
    {
        internal static Color ShellBack => ShellTheme.TaskbarBack;
        internal static Color TopBorder => ShellTheme.TaskbarTopBorder;
        internal static Color BtnDefault => ShellTheme.TaskbarButtonDefault;
        internal static Color BtnFocused => ShellTheme.TaskbarButtonFocused;
        internal static Color BtnHovered => ShellTheme.TaskbarButtonHovered;
        internal static Color BtnPressed => ShellTheme.TaskbarButtonPressed;
        internal static Color BtnBorder => ShellTheme.TaskbarBack;
        internal static Color BtnBorderHot => ShellTheme.TaskbarBack;
        internal static Color TextColor => ShellTheme.TextColor;
    }
}
