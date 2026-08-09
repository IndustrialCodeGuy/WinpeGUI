namespace Shell.Infrastructure.FileTypes;

internal sealed record BuiltInFileTypeDefinition(
    string Extension,
    string DisplayName,
    string KnownType,
    BuiltInFileAssociationKind AssociationKind,
    BuiltInOpenCommandKind CommandKind = BuiltInOpenCommandKind.None,
    string? RegistryProgId = null,
    string? RegistryFriendlyTypeName = null,
    string? PerceivedType = null,
    string? ContentType = null,
    string? RegistryDefaultIcon = null,
    string? RegistryOpenCommand = null)
{
    public string RegistryDisplayName => string.IsNullOrWhiteSpace(RegistryFriendlyTypeName)
        ? DisplayName
        : RegistryFriendlyTypeName;

    public bool RegisterInPe =>
        !string.IsNullOrWhiteSpace(RegistryProgId) &&
        !string.IsNullOrWhiteSpace(RegistryDefaultIcon);
}

internal enum BuiltInFileAssociationKind
{
    TypeOnly,
    EditInNotepad,
    OpenCommand,
    Script,
    RegistryFile,
    Executable
}

internal enum BuiltInOpenCommandKind
{
    None,
    SecurityCatalog,
    Pkcs7Certificate,
    CertificateStore,
    PresentationHost,
    ComExecute,
    PowerShell,
    CommandPrompt,
    WindowsScriptHost,
    RegistryMerge
}
