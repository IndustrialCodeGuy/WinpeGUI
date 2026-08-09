namespace Shell.Core.FileTypes;

public interface IExplorerFileAssociationService
{
    ExplorerFileAssociation ResolveForPath(string path);
    ExplorerFileAssociation ResolveForExtension(string? extension);
}
