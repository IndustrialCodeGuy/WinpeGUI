namespace Explorer.UI.Icons;

internal readonly record struct ExplorerIconKey(
    ExplorerIconSourceKind Kind,
    string Identity,
    int Size,
    bool Hidden)
{
    public ExplorerIconKey WithoutHidden() => this with { Hidden = false };

    public string ImageListKey
    {
        get
        {
            string hiddenSuffix = Hidden ? ":hidden" : string.Empty;

            return Kind switch
            {
                ExplorerIconSourceKind.ThisPc => $"thispc:{Size}{hiddenSuffix}",
                ExplorerIconSourceKind.Drive => $"drive:{Size}:{Identity}{hiddenSuffix}",
                ExplorerIconSourceKind.Folder => $"folder:{Size}{hiddenSuffix}",
                ExplorerIconSourceKind.FilePath => $"file:fixedpath:{Size}:{Identity}{hiddenSuffix}",
                ExplorerIconSourceKind.PathSpecificFile => $"file:path:{Size}:{Identity}{hiddenSuffix}",
                ExplorerIconSourceKind.FileNoExtension => $"file:noext:{Size}{hiddenSuffix}",
                ExplorerIconSourceKind.AssociationHandler => $"file:handler:{Size}:{Identity}{hiddenSuffix}",
                ExplorerIconSourceKind.AssociationKnownType => $"file:known:{Size}:{Identity}{hiddenSuffix}",
                _ => $"unknown:{Size}:{Identity}{hiddenSuffix}"
            };
        }
    }
}