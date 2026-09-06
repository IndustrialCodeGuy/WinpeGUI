using System.Text;

namespace Imaging.Core;

internal static class DiskPartRunner
{
    public static async Task<ProcessExecutionResult> RunAsync(
        string script,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        cancellationToken.ThrowIfCancellationRequested();

        string diskPartPath = Path.Combine(Environment.SystemDirectory, "diskpart.exe");
        if (!File.Exists(diskPartPath))
            return ProcessExecutionResult.Failed("DiskPart.exe was not found under the active Windows system directory.");

        string scriptPath = Path.Combine(Path.GetTempPath(), $"ImagingManager-DiskPart-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(scriptPath, script, Encoding.ASCII, cancellationToken).ConfigureAwait(false);

            ProcessExecutionResult result = await ProcessExecutionRunner.RunAsync(
                diskPartPath,
                new[] { "/s", scriptPath },
                cancellationToken).ConfigureAwait(false);

            if (result.Success && ContainsFailure(result.CombinedOutput))
            {
                return new ProcessExecutionResult(
                    false,
                    result.ExitCode,
                    result.StandardOutput,
                    result.StandardError);
            }

            return result;
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static bool ContainsFailure(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        string[] markers =
        {
            "DiskPart has encountered an error",
            "Virtual Disk Service error",
            "The arguments specified for this command are not valid",
            "There is no disk selected",
            "There is no partition selected",
            "The selected disk is not valid",
            "The selected volume is not valid"
        };

        return markers.Any(marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
