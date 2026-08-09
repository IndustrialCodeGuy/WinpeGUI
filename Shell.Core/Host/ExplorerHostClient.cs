using System.IO.Pipes;
using System.Text.Json;

namespace Shell.Core.Host;

public static class ExplorerHostClient
{
    public static bool TrySignalOpenWindow(ExplorerLaunchRequest request, int timeoutMs = 2_000)
    {
        if (timeoutMs <= 0)
            timeoutMs = 2_000;

        DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadlineUtc)
        {
            try
            {
                using NamedPipeClientStream client = new(
                    ".",
                    ExplorerHostIpc.PipeName,
                    PipeDirection.Out,
                    PipeOptions.None);

                client.Connect(timeout: 250);

                using StreamWriter writer = new(client);
                string json = JsonSerializer.Serialize(request, ExplorerHostIpc.JsonOptions);

                writer.Write(json);
                writer.Flush();

                return true;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }
            catch
            {
                break;
            }

            Thread.Sleep(100);
        }

        return false;
    }
}
