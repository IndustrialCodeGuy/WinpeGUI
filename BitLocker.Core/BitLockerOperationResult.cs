namespace BitLocker.Core;

// Normalized operation result returned by both WMI and manage-bde paths.
public sealed class BitLockerOperationResult
{
    public bool Success { get; init; }
    public uint ReturnCode { get; init; }
    public string Message { get; init; } = string.Empty;

    public static BitLockerOperationResult Ok(string? message = null)
    {
        return new BitLockerOperationResult
        {
            Success = true,
            ReturnCode = 0,
            Message = message ?? string.Empty
        };
    }

    public static BitLockerOperationResult Fail(uint returnCode, string message)
    {
        return new BitLockerOperationResult
        {
            Success = false,
            ReturnCode = returnCode,
            Message = message ?? string.Empty
        };
    }
}
