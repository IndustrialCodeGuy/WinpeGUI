using System.Text.Json;

namespace Imaging.Manager;

internal sealed class PendingWimUnmountState
{
    public string MountDirectory { get; init; } = string.Empty;
    public string ImageFile { get; init; } = string.Empty;
    public int ImageIndex { get; init; }
}

internal static class PendingWimUnmountStateStore
{
    private const string StateFileName = "pending-wim-unmounts.json";

    public static IReadOnlyList<PendingWimUnmountState> Load()
    {
        try
        {
            string path = GetStatePath();
            if (!File.Exists(path))
                return Array.Empty<PendingWimUnmountState>();

            string json = File.ReadAllText(path);
            IReadOnlyList<PendingWimUnmountState>? states =
                JsonSerializer.Deserialize<List<PendingWimUnmountState>>(json);
            return states ?? Array.Empty<PendingWimUnmountState>();
        }
        catch
        {
            return Array.Empty<PendingWimUnmountState>();
        }
    }

    public static void Save(IEnumerable<PendingWimUnmountState> states)
    {
        try
        {
            PendingWimUnmountState[] snapshot = states.ToArray();
            string path = GetStatePath();
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                return;

            Directory.CreateDirectory(directory);
            if (snapshot.Length == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            // The state is a recovery aid, not a prerequisite for DISM itself.
            // In-memory tracking remains active if the WinPE temp location cannot be written.
        }
    }

    private static string GetStatePath() =>
        Path.Combine(Path.GetTempPath(), "Imaging.Manager", StateFileName);
}
