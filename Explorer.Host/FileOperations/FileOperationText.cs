namespace Explorer.Host.FileOperations;

internal static class FileOperationText
{
    public static string GetSizeText(string path)
    {
        try
        {
            if (File.Exists(path))
                return FormatSize(new FileInfo(path).Length);
        }
        catch
        {
        }

        return string.Empty;
    }

    public static string GetDateModifiedText(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
                return File.GetLastWriteTime(path).ToString("M/d/yyyy h:mm tt", System.Globalization.CultureInfo.CurrentCulture);
        }
        catch
        {
        }

        return string.Empty;
    }

    public static string GetDateCreatedText(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
                return File.GetCreationTime(path).ToString("M/d/yyyy h:mm tt", System.Globalization.CultureInfo.CurrentCulture);
        }
        catch
        {
        }

        return string.Empty;
    }

    public static string FormatSize(long bytes)
    {
        const long oneKb = 1024L;
        const long oneMb = oneKb * 1024L;
        const long oneGb = oneMb * 1024L;
        const long oneTb = oneGb * 1024L;

        if (bytes < oneMb)
            return $"{Math.Max(1L, (bytes + oneKb - 1L) / oneKb):N0} KB";

        if (bytes < oneGb)
            return $"{bytes / (double)oneMb:0.##} MB";

        if (bytes < oneTb)
            return $"{bytes / (double)oneGb:0.##} GB";

        return $"{bytes / (double)oneTb:0.##} TB";
    }
}