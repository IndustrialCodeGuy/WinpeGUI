namespace Shell.Core.Models;

public sealed class ExplorerWindowOptions
{
    public string? InitialPath { get; init; }
    public ExplorerWindowMode Mode { get; init; } = ExplorerWindowMode.Browse;
    public string? Title { get; init; }
    public IReadOnlyList<string> AllowedExtensions { get; init; } = Array.Empty<string>();
    public ExplorerWindowPlacement? Placement { get; init; }
    public ExplorerPreloadedDirectoryListing? PreloadedDirectoryListing { get; init; }
}
