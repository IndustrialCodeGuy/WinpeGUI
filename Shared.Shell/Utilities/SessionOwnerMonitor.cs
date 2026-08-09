using System.Runtime.InteropServices;

namespace Shared.Shell.Utilities;

public static class SessionOwnerMonitor
{
    private const uint Synchronize = 0x00100000;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint WaitObject0 = 0x00000000;

    public static void Start(int ownerProcessId, Action ownerExited)
    {
        ArgumentNullException.ThrowIfNull(ownerExited);

        if (ownerProcessId <= 0)
            return;

        IntPtr ownerHandle = OpenProcess(Synchronize, false, ownerProcessId);
        if (ownerHandle == IntPtr.Zero)
        {
            // The owner is already gone or could not be opened. Treat that the
            // same as owner exit, but do it asynchronously so callers can finish
            // their normal startup path first.
            _ = Task.Run(ownerExited);
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                uint result = WaitForSingleObject(ownerHandle, Infinite);
                if (result == WaitObject0)
                    ownerExited();
            }
            finally
            {
                CloseHandle(ownerHandle);
            }
        });
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
