namespace Shell.Core.Models;

public sealed class ExplorerDirectoryItem
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;

    public bool IsDirectory { get; init; }
    public bool IsVisibleHidden { get; init; }

    public string TypeText { get; init; } = string.Empty;
    public string? Extension { get; init; }

    public DateTime? ModifiedLocalTime { get; init; }
    public long? SizeBytes { get; init; }
}