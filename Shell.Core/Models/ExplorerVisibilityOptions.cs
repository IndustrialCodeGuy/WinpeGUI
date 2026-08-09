namespace Shell.Core.Models;

public readonly record struct ExplorerVisibilityOptions(
    bool ShowHidden,
    bool ShowSystem,
    bool ShowSuperHidden)
{
    public static ExplorerVisibilityOptions CurrentDefault =>
        new(
            ShowHidden: true,
            ShowSystem: false,
            ShowSuperHidden: false);
}