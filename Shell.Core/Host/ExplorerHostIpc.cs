using System.Text.Json;

namespace Shell.Core.Host;

public static class ExplorerHostIpc
{
    public const string PipeName = "ExplorerHost.OpenWindow";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
