namespace Shell.Taskbar.Shell
{
    // Simple DTO used by TaskWindowEnumerator.GetTaskWindows().
    // Each instance represents one “taskbar-eligible” top-level window:
    // - Hwnd: native window handle (used for icon lookup + window actions)
    // - Title: current window title (used for button text / truncation logic)
    // - ClassName: window class name (used for filtering / diagnostics)

    internal sealed class TaskWindow
    {
        public IntPtr Hwnd { get; set; }
        public string? Title { get; set; }
        public string? ClassName { get; set; }
    }
}