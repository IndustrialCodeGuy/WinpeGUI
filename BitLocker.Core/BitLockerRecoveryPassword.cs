using System.Text;

namespace BitLocker.Core;

public static class BitLockerRecoveryPassword
{
    public const int DigitCount = 48;
    private const int GroupSize = 6;

    public static string GetDigits(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        StringBuilder digits = new(value.Length);
        foreach (char c in value)
        {
            if (c is >= '0' and <= '9')
                digits.Append(c);
        }

        return digits.ToString();
    }

    public static string GetDigits(char[]? value)
    {
        if (value == null || value.Length == 0)
            return string.Empty;

        StringBuilder digits = new(value.Length);
        foreach (char c in value)
        {
            if (c is >= '0' and <= '9')
                digits.Append(c);
        }

        return digits.ToString();
    }

    public static string FormatForDisplay(string? value)
    {
        string digits = GetDigits(value);
        if (digits.Length == 0)
            return string.Empty;

        StringBuilder formatted = new(digits.Length + 7);
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
                formatted.Append('-');

            formatted.Append(digits[i]);
        }

        return formatted.ToString();
    }

    public static string NormalizeForWmi(char[]? value)
    {
        return FormatForDisplay(GetDigits(value));
    }

    public static bool HasCompleteLength(char[]? value)
    {
        return GetDigits(value).Length == DigitCount;
    }

    public static bool HasCompleteLength(string? value)
    {
        return GetDigits(value).Length == DigitCount;
    }
}
