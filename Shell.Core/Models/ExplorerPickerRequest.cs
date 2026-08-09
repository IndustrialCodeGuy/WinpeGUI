namespace Shell.Core.Models;

public sealed class ExplorerPickerRequest
{
    public ExplorerWindowMode Mode { get; init; } = ExplorerWindowMode.OpenFile;
    public string? InitialPath { get; init; }
    public string? Title { get; init; }
    public long OwnerWindowHandle { get; init; }
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
