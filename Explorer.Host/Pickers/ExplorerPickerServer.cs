using Shell.Core.Models;
using Shell.Core.Pickers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Explorer.Host.Pickers;

internal sealed class ExplorerPickerServer : IDisposable
{
    private readonly Func<ExplorerPickerRequest, CancellationToken, Task<ExplorerPickerResult>> _handleRequestAsync;
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    public ExplorerPickerServer(Func<ExplorerPickerRequest, CancellationToken, Task<ExplorerPickerResult>> handleRequestAsync)
    {
        _handleRequestAsync = handleRequestAsync;
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
                    ExplorerPickerIpc.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);

                NamedPipeServerStream connectedServer = server;
                server = null;

                _ = Task.Run(
                    () => ProcessConnectionAsync(connectedServer, _cts.Token),
                    CancellationToken.None);
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

    private async Task ProcessConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using (server)
        {
            try
            {
                using StreamReader reader = new(
                    server,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1024,
                    leaveOpen: true);

                using StreamWriter writer = new(
                    server,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 1024,
                    leaveOpen: true)
                {
                    AutoFlush = true
                };

                string? requestJson = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                ExplorerPickerRequest request = string.IsNullOrWhiteSpace(requestJson)
                    ? new ExplorerPickerRequest()
                    : JsonSerializer.Deserialize<ExplorerPickerRequest>(
                          requestJson,
                          ExplorerPickerIpc.JsonOptions) ?? new ExplorerPickerRequest();

                ExplorerPickerResult result;

                using (CancellationTokenSource requestCts =
                       CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    Task disconnectMonitorTask = MonitorClientDisconnectAsync(reader, requestCts);

                    result = await _handleRequestAsync(request, requestCts.Token).ConfigureAwait(false);

                    try
                    {
                        requestCts.Cancel();
                    }
                    catch
                    {
                    }

                    try
                    {
                        await disconnectMonitorTask.ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                string responseJson = JsonSerializer.Serialize(result, ExplorerPickerIpc.JsonOptions);
                await writer.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static async Task MonitorClientDisconnectAsync(
    StreamReader reader,
    CancellationTokenSource requestCts)
    {
        try
        {
            string? line = await reader.ReadLineAsync(requestCts.Token).ConfigureAwait(false);

            if (!requestCts.IsCancellationRequested)
                requestCts.Cancel();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            try
            {
                if (!requestCts.IsCancellationRequested)
                    requestCts.Cancel();
            }
            catch
            {
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
