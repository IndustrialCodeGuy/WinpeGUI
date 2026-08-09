namespace Shell.Taskbar.UI
{
    // Simple ToolStripMenuItem that forces a repaint on hover.
    // Useful when using custom renderers/hover visuals where the default invalidate
    // timing can leave the highlight/icon looking “stale” until the next paint.

    internal sealed class InvalidateOnHoverMenuItem(string text) : ToolStripMenuItem(text)
    {
        protected override void OnMouseEnter(EventArgs e)
        {
            Invalidate();
        }
    }
}