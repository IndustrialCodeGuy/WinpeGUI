namespace Imaging.Core;

public static class ImagingPath
{
    public static string NormalizeDriveRoot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string trimmed = value.Trim();
        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            return char.ToUpperInvariant(trimmed[0]) + @":\";

        try
        {
            string? root = Path.GetPathRoot(trimmed);
            if (!string.IsNullOrWhiteSpace(root) && root.Length >= 2 && root[1] == ':')
                return char.ToUpperInvariant(root[0]) + @":\";
        }
        catch
        {
        }

        return string.Empty;
    }

    public static string? TryGetDriveRootForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            string normalized = NormalizeDriveRoot(root);
            return normalized.Length == 0 ? null : normalized;
        }
        catch
        {
            return null;
        }
    }
}
