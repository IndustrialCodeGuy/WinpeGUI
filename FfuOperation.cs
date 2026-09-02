namespace Imaging.Core;

public enum FfuOperationKind
{
    Capture,
    Apply
}

public sealed class FfuOperationProgress
{
    public FfuOperationProgress(int? percentage, string message)
    {
        Percentage = percentage;
        Message = message ?? string.Empty;
    }

    public int? Percentage { get; }
    public string Message { get; }
}

public sealed class FfuOperationResult
{
    public bool Success { get; init; }
    public bool Canceled { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;

    public static FfuOperationResult Completed(int exitCode, string output) => new()
    {
        Success = exitCode == 0,
        Canceled = false,
        ExitCode = exitCode,
        Output = output
    };

    public static FfuOperationResult Cancelled(string output) => new()
    {
        Success = false,
        Canceled = true,
        ExitCode = -1,
        Output = output
    };
}
