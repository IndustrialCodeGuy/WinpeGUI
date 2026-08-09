namespace Shell.Core.Models;

public sealed class ExplorerPreloadedDirectoryListing
{
    public string DirectoryPath { get; init; } = string.Empty;

    public ExplorerPreloadedDirectoryRow[] Rows { get; init; } = [];
}

public readonly struct ExplorerPreloadedDirectoryRow
{
    public ExplorerPreloadedDirectoryRow(
        bool isDirectory,
        string displayName,
        string fullPath,
        string typeText,
        string? extension,
        bool isVisibleHidden,
        DateTime? modifiedLocalTime,
        long? sizeBytes)
    {
        IsDirectory = isDirectory;
        DisplayName = displayName;
        FullPath = fullPath;
        TypeText = typeText;
        Extension = extension;
        IsVisibleHidden = isVisibleHidden;
        ModifiedLocalTime = modifiedLocalTime;
        SizeBytes = sizeBytes;
    }

    public bool IsDirectory { get; }
    public string DisplayName { get; }
    public string FullPath { get; }
    public string TypeText { get; }
    public string? Extension { get; }
    public bool IsVisibleHidden { get; }
    public DateTime? ModifiedLocalTime { get; }
    public long? SizeBytes { get; }
}