namespace BitLocker.Core;

// Structured description of a BitLocker key protector. Keep the WMI numeric
// type so newer Windows versions can still be represented even when the local
// display mapping does not know a newer protector name yet.
public sealed class BitLockerKeyProtectorInfo
{
    public string Id { get; init; } = string.Empty;
    public uint TypeCode { get; init; }
    public string TypeText { get; init; } = "Unknown";
    public string FriendlyName { get; init; } = string.Empty;

    public string IdPrefix
    {
        get
        {
            string cleaned = Id.Trim().Trim('{', '}');
            return cleaned.Length <= 8 ? cleaned : cleaned[..8];
        }
    }

    public string DisplayText
    {
        get
        {
            string idText = string.IsNullOrWhiteSpace(Id)
                ? string.Empty
                : $" {{{Id.Trim().Trim('{', '}')}}}";

            if (string.IsNullOrWhiteSpace(FriendlyName))
                return TypeText + idText;

            return $"{TypeText} - {FriendlyName}{idText}";
        }
    }
}
