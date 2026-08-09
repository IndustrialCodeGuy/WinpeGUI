using System.Management;

namespace BitLocker.Core;

public static class BitLockerWmiReader
{
    public static uint? TryReadSingleUInt32OutParam(
        ManagementObject volume,
        string methodName,
        string outParamName)
    {
        try
        {
            using ManagementBaseObject? outParams = volume.InvokeMethod(methodName, null, null);
            if (outParams == null)
                return null;

            if (!TryReadUInt32Property(outParams, "ReturnValue", out uint returnValue) ||
                returnValue != 0)
            {
                return null;
            }

            return TryReadUInt32Property(outParams, outParamName, out uint outValue)
                ? outValue
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool TryReadUInt32Property(
        ManagementBaseObject obj,
        string propertyName,
        out uint value)
    {
        value = 0;

        try
        {
            object? rawValue = obj[propertyName];
            if (rawValue == null)
                return false;

            value = Convert.ToUInt32(rawValue);
            return true;
        }
        catch
        {
            return false;
        }
    }
}