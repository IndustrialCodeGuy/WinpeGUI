namespace Shell.Core.FileTypes;

public sealed record ExplorerFileVerb(
    string Id,
    string DisplayName,
    ExplorerOpenCommand Command);
