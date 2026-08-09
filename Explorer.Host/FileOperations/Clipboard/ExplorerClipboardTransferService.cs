using WinFormsClipboard = System.Windows.Forms.Clipboard;
using System.Collections.Specialized;
using System.Text.Json;

namespace Explorer.Host.FileOperations.Clipboard;

internal static class ExplorerClipboardTransferService
{
    private const string TransferFormat = "WinPeShell.ExplorerFileTransfer";
    private const string PreferredDropEffectFormat = "Preferred DropEffect";

    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

    public static void SetFileTransfer(IReadOnlyList<string> sourcePaths, bool move)
    {
        List<string> normalizedPaths = NormalizePaths(sourcePaths);
        if (normalizedPaths.Count == 0)
            return;

        ExplorerTransferManifest manifest = new()
        {
            SourcePaths = normalizedPaths,
            Move = move
        };

        DataObject dataObject = new();
        dataObject.SetData(TransferFormat, JsonSerializer.Serialize(manifest));

        StringCollection fileDropList = [.. normalizedPaths.ToArray()];
        dataObject.SetFileDropList(fileDropList);

        dataObject.SetData(PreferredDropEffectFormat, BitConverter.GetBytes(move ? DropEffectMove : DropEffectCopy));

        WinFormsClipboard.SetDataObject(dataObject, true);
    }

    public static bool TryClear()
    {
        try
        {
            WinFormsClipboard.Clear();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetFileTransfer(out ExplorerTransferManifest? manifest)
    {
        manifest = null;

        try
        {
            IDataObject? dataObject = WinFormsClipboard.GetDataObject();
            if (dataObject is null)
                return false;

            if (TryReadCustomManifest(dataObject, out manifest))
                return true;

            if (TryReadFileDropManifest(dataObject, out manifest))
                return true;
        }
        catch
        {
        }

        manifest = null;
        return false;
    }

    private static bool TryReadCustomManifest(IDataObject dataObject, out ExplorerTransferManifest? manifest)
    {
        manifest = null;

        if (!dataObject.GetDataPresent(TransferFormat))
            return false;

        object? raw = dataObject.GetData(TransferFormat);
        if (raw is not string json || string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            ExplorerTransferManifest? parsed = JsonSerializer.Deserialize<ExplorerTransferManifest>(json);
            if (parsed is null)
                return false;

            List<string> normalizedPaths = NormalizePaths(parsed.SourcePaths);
            if (normalizedPaths.Count == 0)
                return false;

            manifest = new ExplorerTransferManifest
            {
                Version = parsed.Version,
                SourcePaths = normalizedPaths,
                Move = parsed.Move
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadFileDropManifest(IDataObject dataObject, out ExplorerTransferManifest? manifest)
    {
        manifest = null;

        if (!dataObject.GetDataPresent(DataFormats.FileDrop))
            return false;

        object? raw = dataObject.GetData(DataFormats.FileDrop);

        if (raw is not string[] fileDropPaths || fileDropPaths.Length == 0)
            return false;

        List<string> normalizedPaths = NormalizePaths(fileDropPaths);
        if (normalizedPaths.Count == 0)
            return false;

        manifest = new ExplorerTransferManifest
        {
            SourcePaths = normalizedPaths,
            Move = ReadPreferredDropEffect(dataObject) == DropEffectMove
        };

        return true;
    }

    private static int ReadPreferredDropEffect(IDataObject dataObject)
    {
        try
        {
            if (!dataObject.GetDataPresent(PreferredDropEffectFormat))
                return DropEffectCopy;

            object? raw = dataObject.GetData(PreferredDropEffectFormat);

            if (raw is MemoryStream stream)
            {
                byte[] bytes = stream.ToArray();
                if (bytes.Length >= sizeof(int))
                    return BitConverter.ToInt32(bytes, 0);
            }

            if (raw is byte[] buffer && buffer.Length >= sizeof(int))
                return BitConverter.ToInt32(buffer, 0);
        }
        catch
        {
        }

        return DropEffectCopy;
    }

    private static List<string> NormalizePaths(IEnumerable<string> sourcePaths)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> normalized = [];

        foreach (string? path in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            string candidate = path.Trim();

            try
            {
                candidate = Path.GetFullPath(candidate);
            }
            catch
            {
            }

            if (!seen.Add(candidate))
                continue;

            normalized.Add(candidate);
        }

        return normalized;
    }
}
