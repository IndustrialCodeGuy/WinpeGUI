namespace Shell.Core.Models;

public sealed class ExplorerDirectoryListing
{
    public string DirectoryPath { get; init; } = string.Empty;
    public IReadOnlyList<ExplorerDirectoryItem> Items { get; init; } = Array.Empty<ExplorerDirectoryItem>();
}