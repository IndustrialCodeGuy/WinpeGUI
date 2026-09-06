using System.Diagnostics;

namespace Imaging.Core;

internal readonly record struct ProcessExecutionResult(
    bool Success,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput, StandardError }.Where(static value => !string.IsNullOrWhiteSpace(value)));

    public static ProcessExecutionResult Failed(string message) =>
        new(false, -1, string.Empty, message);
}

internal static class ProcessExecutionRunner
{
    public static async Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(fileName) ?? Environment.SystemDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {Path.GetFileName(fileName)}.");

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        using CancellationTokenRegistration registration = cancellationToken.Register(() => TryKill(process));
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            try { await process.WaitForExitAsync().ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); } catch { }
            throw;
        }

        string output = (await stdoutTask.ConfigureAwait(false)).Trim();
        string error = (await stderrTask.ConfigureAwait(false)).Trim();
        return new ProcessExecutionResult(process.ExitCode == 0, process.ExitCode, output, error);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}
