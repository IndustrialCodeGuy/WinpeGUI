namespace Imaging.Core;

public sealed class WimOperationProgress
{
    public WimOperationProgress(int? percentage, string message)
    {
        Percentage = percentage;
        Message = message ?? string.Empty;
    }

    public int? Percentage { get; }
    public string Message { get; }
}

public sealed class WimOperationResult
{
    public bool Success { get; init; }
    public bool Canceled { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;

    public static WimOperationResult Completed(int exitCode, string output) => new()
    {
        Success = exitCode == 0,
        Canceled = false,
        ExitCode = exitCode,
        Output = output
    };

    public static WimOperationResult Cancelled(string output) => new()
    {
        Success = false,
        Canceled = true,
        ExitCode = -1,
        Output = output
    };
}

public sealed class WimImageInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? $"Index {Index}"
        : $"{Index}: {Name}";
}

public sealed class WimImageInfoResult
{
    public bool Success { get; init; }
    public bool Canceled { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
    public IReadOnlyList<WimImageInfo> Images { get; init; } = Array.Empty<WimImageInfo>();
}
