namespace Shell.Core.FileTypes;

public sealed record ExplorerFileAssociation(
    string Extension,
    string DisplayName,
    ExplorerFileIconIdentity IconIdentity,
    ExplorerOpenCommand? DefaultOpenCommand,
    IReadOnlyList<ExplorerOpenCommand> OpenWithCommands,
    IReadOnlyList<ExplorerFileVerb> ExtraVerbs,
    bool IsUserDefined)
{
    public bool HasDefaultOpenCommand => DefaultOpenCommand is not null;
}
