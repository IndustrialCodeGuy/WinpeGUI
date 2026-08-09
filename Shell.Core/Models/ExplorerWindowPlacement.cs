using System.Drawing;

namespace Shell.Core.Models;

public sealed class ExplorerWindowPlacement
{
    public Rectangle Bounds { get; init; }
    public bool IsMaximized { get; init; }
    public int NavPaneWidthDip { get; init; }
}