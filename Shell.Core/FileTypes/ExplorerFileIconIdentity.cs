namespace Shell.Core.FileTypes;

public readonly record struct ExplorerFileIconIdentity(
    ExplorerFileIconIdentityKind Kind,
    string Value)
{
    public static ExplorerFileIconIdentity GenericNoExtension { get; } =
        new(ExplorerFileIconIdentityKind.GenericNoExtension, "file");
}

public enum ExplorerFileIconIdentityKind
{
    GenericNoExtension,
    Handler,
    KnownType,
    FilePath
}
