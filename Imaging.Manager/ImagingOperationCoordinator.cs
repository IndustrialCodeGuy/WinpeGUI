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

        string? diskIdentity = disk == null ? null : GetDiskIdentity(disk);
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
        string diskIdentity = GetDiskIdentity(disk);

        lock (_sync)
        {
            if (_activeOperation != null &&
                _activeOperation.DiskNumber.HasValue &&
                (string.Equals(_activeOperation.DiskIdentity, diskIdentity, StringComparison.OrdinalIgnoreCase) ||
                 _activeOperation.DiskNumber.Value == disk.DiskNumber))
            {
                operationName = _activeOperation.Name;
                return true;
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

    private static string GetDiskIdentity(ImagingDiskInfo disk)
    {
        string stableId = FirstNonEmpty(
            disk.StorageInfo?.UniqueId,
            disk.StorageInfo?.Guid,
            disk.StorageInfo?.SerialNumber,
            disk.SerialNumber,
            disk.DevicePath);

        return string.IsNullOrWhiteSpace(stableId)
            ? $"number:{disk.DiskNumber}"
            : $"stable:{stableId.Trim()}";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed record ActiveOperation(
        string Name,
        string? DiskIdentity,
        int? DiskNumber);
}
