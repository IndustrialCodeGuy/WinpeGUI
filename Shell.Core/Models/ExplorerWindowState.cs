namespace Shell.Core.Models;

public sealed class ExplorerWindowState
{
    public string WindowId { get; init; } = string.Empty;
    public string? CurrentPath { get; init; }
    public string? CurrentDriveRoot { get; init; }
    public bool IsThisPcView { get; init; }
}