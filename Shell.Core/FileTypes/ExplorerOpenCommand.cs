namespace Shell.Core.FileTypes;

public sealed record ExplorerOpenCommand(
    string Id,
    string DisplayName,
    string ExecutablePath,
    string Arguments,
    ExplorerFileIconIdentity? IconIdentity = null);
