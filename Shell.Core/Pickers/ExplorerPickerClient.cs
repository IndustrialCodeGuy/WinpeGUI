using Shell.Core.Models;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Shell.Core.Pickers;

public static class ExplorerPickerClient
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);

    public static string? PickOpenFile(
        string? initialPath = null,
        string? title = null,
        IEnumerable<string>? allowedExtensions = null,
        TimeSpan? connectTimeout = null,
        IntPtr ownerWindowHandle = default)
    {
        ExplorerPickerResult result = Pick(new ExplorerPickerRequest
        {
            Mode = ExplorerWindowMode.OpenFile,
            InitialPath = initialPath,
            Title = title,
            OwnerWindowHandle = ownerWindowHandle.ToInt64(),
            AllowedExtensions = ToExtensionArray(allowedExtensions)
        }, connectTimeout);

        return result.Accepted ? result.SelectedPath : null;
    }

    public static string? PickFolder(
        string? initialPath = null,
        string? title = null,
        TimeSpan? connectTimeout = null,
        IntPtr ownerWindowHandle = default)
    {
        ExplorerPickerResult result = Pick(new ExplorerPickerRequest
        {
            Mode = ExplorerWindowMode.SelectFolder,
            InitialPath = initialPath,
            Title = title,
            OwnerWindowHandle = ownerWindowHandle.ToInt64()
        }, connectTimeout);

        return result.Accepted ? result.SelectedPath : null;
    }

    public static string? PickSaveFile(
        string? initialPath = null,
        string? title = null,
        IEnumerable<string>? allowedExtensions = null,
        TimeSpan? connectTimeout = null,
        IntPtr ownerWindowHandle = default)
    {
        ExplorerPickerResult result = Pick(new ExplorerPickerRequest
        {
            Mode = ExplorerWindowMode.SaveFile,
            InitialPath = initialPath,
            Title = title,
            OwnerWindowHandle = ownerWindowHandle.ToInt64(),
            AllowedExtensions = ToExtensionArray(allowedExtensions)
        }, connectTimeout);

        return result.Accepted ? result.SelectedPath : null;
    }

    public static async Task<string?> PickOpenFileAsync(
        string? initialPath = null,
        string? title = null,
        IEnumerable<string>? allowedExtensions = null,
        TimeSpan? connectTimeout = null,
        IntPtr ownerWindowHandle = default,
        CancellationToken cancellationToken = default)
    {
        ExplorerPickerResult result = await PickAsync(new ExplorerPickerRequest
        {
            Mode = ExplorerWindowMode.OpenFile,
            InitialPath = initialPath,
            Title = title,
            OwnerWindowHandle = ownerWindowHandle.ToInt64(),
            AllowedExtensions = ToExtensionArray(allowedExtensions)
        }, connectTimeout, cancellationToken).ConfigureAwait(false);

        return result.Accepted ? result.SelectedPath : null;
    }

    public static async Task<string?> PickFolderAsync(
        string? initialPath = null,
        string? title = null,
        TimeSpan? connectTimeout = null,
        IntPtr ownerWindowHandle = default,
        CancellationToken cancellationToken = default)
    {
        ExplorerPickerResult result = await PickAsync(new ExplorerPickerRequest
        {
            Mode = ExplorerWindowMode.SelectFolder,
            InitialPath = initialPath,
            Title = title,
            OwnerWindowHandle = ownerWindowHandle.ToInt64()
        }, connectTimeout, cancellationToken).ConfigureAwait(false);

        return result.Accepted ? result.SelectedPath : null;
    }

    public static async Task<string?> PickSaveFileAsync(
        string? initialPath = null,
        string? title = null,
        IEnumerable<string>? allowedExtensions = null,
        TimeSpan? connectTimeout = null,
        IntPtr ownerWindowHandle = default,
        CancellationToken cancellationToken = default)
    {
        ExplorerPickerResult result = await PickAsync(new ExplorerPickerRequest
        {
            Mode = ExplorerWindowMode.SaveFile,
            InitialPath = initialPath,
            Title = title,
            OwnerWindowHandle = ownerWindowHandle.ToInt64(),
            AllowedExtensions = ToExtensionArray(allowedExtensions)
        }, connectTimeout, cancellationToken).ConfigureAwait(false);

        return result.Accepted ? result.SelectedPath : null;
    }

    public static ExplorerPickerResult Pick(
        ExplorerPickerRequest request,
        TimeSpan? connectTimeout = null,
        CancellationToken cancellationToken = default)
    {
        return PickAsync(request, connectTimeout, cancellationToken).GetAwaiter().GetResult();
    }

    public static async Task<ExplorerPickerResult> PickAsync(
    ExplorerPickerRequest request,
    TimeSpan? connectTimeout = null,
    CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new ExplorerPickerRequest();
            TimeSpan timeout = connectTimeout ?? DefaultConnectTimeout;

            using NamedPipeClientStream client = new(
                ".",
                ExplorerPickerIpc.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            using (CancellationTokenSource timeoutCts = new(timeout))
            using (CancellationTokenSource linkedConnectCts = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken,
                       timeoutCts.Token))
            {
                try
                {
                    await client.ConnectAsync(linkedConnectCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return ExplorerPickerResult.Error("Timed out connecting to the Explorer picker.");
                }
            }

            using StreamWriter writer = new(
                client,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 1024,
                leaveOpen: true)
            {
                AutoFlush = true
            };

            using StreamReader reader = new(
                client,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1024,
                leaveOpen: true);

            string requestJson = JsonSerializer.Serialize(request, ExplorerPickerIpc.JsonOptions);
            await writer.WriteLineAsync(requestJson.AsMemory(), cancellationToken).ConfigureAwait(false);

            string? responseJson = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseJson))
                return ExplorerPickerResult.Error("The Explorer picker returned an empty response.");

            return JsonSerializer.Deserialize<ExplorerPickerResult>(
                       responseJson,
                       ExplorerPickerIpc.JsonOptions)
                   ?? ExplorerPickerResult.Error("The Explorer picker returned an invalid response.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ExplorerPickerResult.Error("The Explorer picker request was canceled.");
        }
        catch (Exception ex)
        {
            return ExplorerPickerResult.Error($"Unable to contact the Explorer picker.\n\n{ex.Message}");
        }
    }

    private static string[] ToExtensionArray(IEnumerable<string>? allowedExtensions)
    {
        return allowedExtensions?
            .Where(static extension => !string.IsNullOrWhiteSpace(extension))
            .Select(static extension => extension.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();
    }
}
