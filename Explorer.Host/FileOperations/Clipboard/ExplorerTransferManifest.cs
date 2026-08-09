namespace Explorer.Host.FileOperations.Clipboard;

internal sealed class ExplorerTransferManifest
{
    public int Version { get; init; } = 1;

    public List<string> SourcePaths { get; init; } = [];

    public bool Move { get; init; }
}