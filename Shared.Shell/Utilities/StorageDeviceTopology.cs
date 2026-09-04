using System.Runtime.InteropServices;
using System.Text;

namespace Shared.Shell.Utilities;

/// <summary>
/// Small ConfigMgr-based helpers for classifying a storage device by its
/// physical PnP ancestry. This is intentionally independent of logical
/// DriveType and storage-provider BusType because USB-attached SATA/NVMe
/// devices frequently report themselves as fixed disks and can surface as
/// SCSI/SATA/NVMe through a bridge.
/// </summary>
public static class StorageDeviceTopology
{
    private const int CR_SUCCESS = 0;
    private const int CM_LOCATE_DEVNODE_NORMAL = 0;
    private const int MaxDeviceInstanceIdLength = 200;

    /// <summary>
    /// Returns true only when the device can be resolved and a USB-storage
    /// ancestor is found before the local PCI/USB-root boundary.
    /// </summary>
    public static bool IsUsbAttachedStorageDevice(string pnpDeviceId)
    {
        return TryIsUsbAttachedStorageDevice(pnpDeviceId, out bool isUsbAttached) && isUsbAttached;
    }

    /// <summary>
    /// Walks the device's PnP parent chain. A USBSTOR, UASPSTOR, or non-root
    /// USB ancestor means the storage device is physically behind USB. Hitting
    /// PCI or USB\ROOT first means it is not USB-attached. The return value is
    /// false only when the ancestry could not be resolved reliably.
    /// </summary>
    public static bool TryIsUsbAttachedStorageDevice(string pnpDeviceId, out bool isUsbAttached)
    {
        isUsbAttached = false;

        if (string.IsNullOrWhiteSpace(pnpDeviceId))
            return false;

        try
        {
            if (CM_Locate_DevNodeW(out uint devInst, pnpDeviceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
                return false;

            uint currentDevInst = devInst;
            HashSet<uint> visitedDevInsts = [];

            while (visitedDevInsts.Add(currentDevInst))
            {
                if (!TryGetDeviceInstanceId(currentDevInst, out string currentDeviceId))
                    return false;

                if (IsUsbStorageInstanceId(currentDeviceId))
                {
                    isUsbAttached = true;
                    return true;
                }

                if (IsHardParentBoundary(currentDeviceId))
                {
                    isUsbAttached = false;
                    return true;
                }

                if (CM_Get_Parent(out uint parentDevInst, currentDevInst, 0) != CR_SUCCESS ||
                    parentDevInst == currentDevInst)
                {
                    // Reaching the top without encountering USB is a resolved
                    // non-USB result, matching the behavior used for eject UI.
                    isUsbAttached = false;
                    return true;
                }

                currentDevInst = parentDevInst;
            }

            isUsbAttached = false;
            return true;
        }
        catch
        {
            isUsbAttached = false;
            return false;
        }
    }

    private static bool TryGetDeviceInstanceId(uint devInst, out string instanceId)
    {
        StringBuilder buffer = new(MaxDeviceInstanceIdLength);

        if (CM_Get_Device_IDW(devInst, buffer, buffer.Capacity, 0) != CR_SUCCESS)
        {
            instanceId = string.Empty;
            return false;
        }

        instanceId = buffer.ToString();
        return !string.IsNullOrWhiteSpace(instanceId);
    }

    private static bool IsUsbStorageInstanceId(string instanceId)
    {
        if (instanceId.StartsWith(@"USBSTOR\", StringComparison.OrdinalIgnoreCase) ||
            instanceId.StartsWith(@"UASPSTOR\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return instanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) &&
            !instanceId.StartsWith(@"USB\ROOT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHardParentBoundary(string instanceId)
    {
        return instanceId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) ||
            instanceId.StartsWith(@"USB\ROOT", StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(
        out uint pdnDevInst,
        string pDeviceID,
        int ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(
        out uint pdnDevInst,
        uint dnDevInst,
        int ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(
        uint dnDevInst,
        StringBuilder buffer,
        int bufferLen,
        int ulFlags);
}
