namespace BitLocker.Core;

// Shared drive-path normalization used by launch arguments, mutex names, and
// backend lookups.
public static class BitLockerDrivePath
{
    public static string? NormalizeDrivePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string trimmed = value.Trim().Trim('"').Trim();

        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            return trimmed + @"\";

        string? root = Path.GetPathRoot(trimmed);
        if (!string.IsNullOrWhiteSpace(root))
            return root;

        return trimmed;
    }

    public static string ToSafeName(string drivePath)
    {
        string normalized = drivePath.Trim().ToUpperInvariant();
        char[] chars = normalized
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();

        return new string(chars);
    }
}
