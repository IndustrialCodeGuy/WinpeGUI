using Shell.Core.FileTypes;
using System.Collections.Concurrent;

namespace Shell.Infrastructure.FileTypes;

public sealed class ExplorerFileAssociationService : IExplorerFileAssociationService
{
    private static readonly ExplorerFileAssociation NoExtensionAssociation = new(
        string.Empty,
        "File",
        ExplorerFileIconIdentity.GenericNoExtension,
        null,
        [],
        [],
        IsUserDefined: false);

    private readonly IReadOnlyDictionary<string, ExplorerFileAssociation> _builtInAssociations;
    private readonly ConcurrentDictionary<string, ExplorerFileAssociation> _fallbackAssociations = new(StringComparer.OrdinalIgnoreCase);

    public ExplorerFileAssociationService()
    {
        _builtInAssociations = ExplorerBuiltInFileAssociations.Create();
    }

    public ExplorerFileAssociation ResolveForPath(string path)
    {
        return ResolveForExtension(Path.GetExtension(path));
    }

    public ExplorerFileAssociation ResolveForExtension(string? extension)
    {
        string normalizedExtension = NormalizeExtension(extension);

        if (string.IsNullOrEmpty(normalizedExtension))
            return NoExtensionAssociation;

        if (_builtInAssociations.TryGetValue(normalizedExtension, out ExplorerFileAssociation? association))
            return association;

        return _fallbackAssociations.GetOrAdd(
            normalizedExtension,
            static ext => new ExplorerFileAssociation(
                ext,
                ext.ToUpperInvariant() + " File",
                ExplorerFileIconIdentity.GenericNoExtension,
                null,
                [],
                [],
                IsUserDefined: false));
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        extension = extension.Trim();
        return extension.StartsWith('.') ? extension : "." + extension;
    }
}
