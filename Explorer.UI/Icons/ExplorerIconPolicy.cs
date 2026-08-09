using Explorer.UI.Shell;
using Shared.Shell.Models;
using Shell.Core.FileTypes;

namespace Explorer.UI.Icons;

internal sealed class ExplorerIconPolicy
{
    private readonly IExplorerFileAssociationService _fileAssociations;
    private readonly Dictionary<string, ExplorerFileIconIdentity> _fileIconIdentityByExtension = new(StringComparer.OrdinalIgnoreCase);

    public ExplorerIconPolicy(IExplorerFileAssociationService fileAssociations)
    {
        _fileAssociations = fileAssociations;
    }

    public static ExplorerIconKey GetThisPcIconKey(int size)
    {
        return new ExplorerIconKey(
            ExplorerIconSourceKind.ThisPc,
            string.Empty,
            ExplorerIconRules.NormalizeSize(size),
            false);
    }

    public static ExplorerIconKey GetDriveIconKey(DriveVisualKind visualKind, int size)
    {
        return new ExplorerIconKey(
            ExplorerIconSourceKind.Drive,
            visualKind.ToString(),
            ExplorerIconRules.NormalizeSize(size),
            false);
    }

    public static ExplorerIconKey GetFolderIconKey(int size, bool hidden)
    {
        return new ExplorerIconKey(
            ExplorerIconSourceKind.Folder,
            string.Empty,
            ExplorerIconRules.NormalizeSize(size),
            hidden);
    }

    public ExplorerIconKey GetListIconKey(ExplorerListRow row, int size)
    {
        return row.Kind switch
        {
            ExplorerListRowKind.Drive => GetDriveIconKey(row.DriveVisualKind ?? DriveVisualKind.Fixed, size),
            ExplorerListRowKind.Directory => GetFolderIconKey(size, row.IsVisibleHidden),
            _ => GetFileIconKey(row.Extension, row.IsVisibleHidden, size)
        };
    }

    private ExplorerIconKey GetFileIconKey(string? extension, bool hidden, int size)
    {
        ExplorerFileIconIdentity identity = GetFileIconIdentity(extension);
        return ToIconKey(identity, hidden, size);
    }

    private ExplorerFileIconIdentity GetFileIconIdentity(string? extension)
    {
        string normalizedExtension = ExplorerIconRules.NormalizeExtension(extension);

        if (_fileIconIdentityByExtension.TryGetValue(normalizedExtension, out ExplorerFileIconIdentity identity))
            return identity;

        identity = _fileAssociations.ResolveForExtension(normalizedExtension).IconIdentity;
        _fileIconIdentityByExtension[normalizedExtension] = identity;
        return identity;
    }

    private static ExplorerIconKey ToIconKey(
        ExplorerFileIconIdentity identity,
        bool hidden,
        int size)
    {
        int normalizedSize = ExplorerIconRules.NormalizeSize(size);

        return identity.Kind switch
        {
            ExplorerFileIconIdentityKind.Handler => new ExplorerIconKey(
                ExplorerIconSourceKind.AssociationHandler,
                ExplorerIconRules.NormalizeIdentity(identity.Value),
                normalizedSize,
                hidden),

            ExplorerFileIconIdentityKind.KnownType => new ExplorerIconKey(
                ExplorerIconSourceKind.AssociationKnownType,
                ExplorerIconRules.NormalizeIdentity(identity.Value),
                normalizedSize,
                hidden),


            ExplorerFileIconIdentityKind.FilePath => new ExplorerIconKey(
                ExplorerIconSourceKind.FilePath,
                identity.Value,
                normalizedSize,
                hidden),

            _ => new ExplorerIconKey(
                ExplorerIconSourceKind.FileNoExtension,
                string.Empty,
                normalizedSize,
                hidden)
        };
    }

    public static bool TryGetPathSpecificListIconKey(
    ExplorerListRow row,
    int size,
    out ExplorerIconKey key)
    {
        key = default;

        // Path-specific extraction is intentionally limited to visible rows.
        // First-render icons should stay association/type based so large folders remain responsive.
        if (row.Kind is ExplorerListRowKind.Drive or ExplorerListRowKind.Directory)
            return false;

        if (string.IsNullOrWhiteSpace(row.FullPath))
            return false;

        if (!ExplorerIconRules.UsesPathSpecificFileIcon(row.Extension))
            return false;

        key = new ExplorerIconKey(
            ExplorerIconSourceKind.PathSpecificFile,
            row.FullPath,
            ExplorerIconRules.NormalizeSize(size),
            row.IsVisibleHidden);

        return true;
    }
}
