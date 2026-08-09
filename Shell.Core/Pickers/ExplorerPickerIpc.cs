using System.Text.Json;

namespace Shell.Core.Pickers;

public static class ExplorerPickerIpc
{
    public const string PipeName = "ExplorerHost.Picker";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
