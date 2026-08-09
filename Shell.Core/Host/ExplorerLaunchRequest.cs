using Shell.Core.Models;

namespace Shell.Core.Host;

public sealed class ExplorerLaunchRequest
{
    public string? InitialPath { get; init; }
    public ExplorerWindowMode Mode { get; init; } = ExplorerWindowMode.Browse;
    public bool HostOnly { get; init; }
    public string? Title { get; init; }
    public string[] AllowedExtensions { get; init; } = Array.Empty<string>();

    public ExplorerWindowOptions ToWindowOptions()
    {
        return new ExplorerWindowOptions
        {
            InitialPath = InitialPath,
            Mode = Mode,
            Title = Title,
            AllowedExtensions = AllowedExtensions
        };
    }
}
