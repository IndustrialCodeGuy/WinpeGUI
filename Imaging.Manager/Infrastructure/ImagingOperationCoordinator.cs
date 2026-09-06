using Imaging.Core;

namespace Imaging.Manager;

/// <summary>
/// Owns the single active Imaging Manager operation. Imaging/servicing work is
/// intentionally serialized; the optional disk identity is retained so live
/// inventory refreshes can continue to show which disk is busy.
/// </summary>
internal sealed class ImagingOperationCoordinator
{
    private readonly object _sync = new();
    private ActiveOperation? _activeOperation;

    public bool IsOperationActive
    {
        get
        {
            lock (_sync)
                return _activeOperation is not null;
        }
    }

    public bool TryBegin(string operationName, ImagingDiskInfo? disk = null)
    {
        if (string.IsNullOrWhiteSpace(operationName))
            operationName = "Imaging operation";

        string? diskIdentity = disk?.StableIdentity;
        int? diskNumber = disk?.DiskNumber;

        lock (_sync)
        {
            if (_activeOperation is not null)
                return false;

            _activeOperation = new ActiveOperation(
                operationName.Trim(),
                diskIdentity,
                diskNumber);
            return true;
        }
    }

    public bool TryGetDiskOperationName(ImagingDiskInfo disk, out string operationName)
    {
        string diskIdentity = disk.StableIdentity;

        lock (_sync)
        {
            if (_activeOperation != null && _activeOperation.DiskNumber.HasValue)
            {
                bool stableIdentityMatches = string.Equals(
                    _activeOperation.DiskIdentity,
                    diskIdentity,
                    StringComparison.OrdinalIgnoreCase);
                bool numberFallbackMatches =
                    _activeOperation.DiskIdentity?.StartsWith("number:", StringComparison.OrdinalIgnoreCase) == true &&
                    _activeOperation.DiskNumber.Value == disk.DiskNumber;

                if (stableIdentityMatches || numberFallbackMatches)
                {
                    operationName = _activeOperation.Name;
                    return true;
                }
            }
        }

        operationName = string.Empty;
        return false;
    }

    public void End()
    {
        lock (_sync)
            _activeOperation = null;
    }

    private sealed record ActiveOperation(
        string Name,
        string? DiskIdentity,
        int? DiskNumber);
}
