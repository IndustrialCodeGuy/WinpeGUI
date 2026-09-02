namespace Imaging.Core;

public enum WimDeploymentFirmwareType
{
    Unknown = 0,
    Bios = 1,
    Uefi = 2
}

public sealed class WimDeploymentProgress
{
    public WimDeploymentProgress(int? percentage, string message)
    {
        Percentage = percentage;
        Message = message ?? string.Empty;
    }

    public int? Percentage { get; }
    public string Message { get; }
}

public sealed class WimDeploymentResult
{
    public bool Success { get; init; }
    public bool Canceled { get; init; }
    public string Output { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public WimDeploymentFirmwareType FirmwareType { get; init; }
}


public sealed class WimBootConfigurationResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
}
