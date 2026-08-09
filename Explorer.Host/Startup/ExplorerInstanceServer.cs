using Shell.Core.Host;
using System.IO.Pipes;
using System.Text.Json;

namespace Explorer.Host.Startup;

internal sealed class ExplorerInstanceServer : IDisposable
{
    private readonly Action<ExplorerLaunchRequest> _onOpenWindowRequested;
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    public ExplorerInstanceServer(Action<ExplorerLaunchRequest> onOpenWindowRequested)
    {
        _onOpenWindowRequested = onOpenWindowRequested;
    }

    public void Start()
    {
        _serverTask = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;

            try
            {
                server = new NamedPipeServerStream(
                    ExplorerHostIpc.PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token);

                using StreamReader reader = new(
                    server,
                    System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1024,
                    leaveOpen: true);

                string json = await reader.ReadToEndAsync();

                ExplorerLaunchRequest? request = null;

                if (!string.IsNullOrWhiteSpace(json))
                {
                    request = JsonSerializer.Deserialize<ExplorerLaunchRequest>(
                        json,
                        ExplorerHostIpc.JsonOptions);
                }

                _onOpenWindowRequested(request ?? new ExplorerLaunchRequest());
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }
            finally
            {
                try
                {
                    server?.Dispose();
                }
                catch
                {
                }
            }
        }
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        try
        {
            _serverTask?.Wait(500);
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
