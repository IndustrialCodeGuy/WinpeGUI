namespace Explorer.UI.Icons;

internal static class ExplorerIconRules
{
    public static int NormalizeSize(int size)
    {
        return size > 0 ? size : 16;
    }

    public static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        extension = extension.Trim();
        return (extension.StartsWith('.') ? extension : "." + extension).ToLowerInvariant();
    }

    public static string NormalizeIdentity(string? identity)
    {
        return string.IsNullOrWhiteSpace(identity)
            ? string.Empty
            : identity.Trim().ToLowerInvariant();
    }

    public static bool UsesPathSpecificFileIcon(string? extension)
    {
        return NormalizeExtension(extension) is ".exe" or ".ico" or ".lnk" or ".url";
    }

    public static bool SupportsDirectPathSpecificExtraction(string? extension)
    {
        return NormalizeExtension(extension) is ".exe" or ".dll" or ".ico";
    }
}