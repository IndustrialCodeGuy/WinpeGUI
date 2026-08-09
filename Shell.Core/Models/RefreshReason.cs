namespace Shell.Core.Models;

public enum RefreshReason
{
    Unknown = 0,
    Startup,
    DeviceArrival,
    DeviceRemoval,
    DeviceNodesChanged,
    BitLockerStateChanged,
    ManualRefresh,
    InternalRequest
}