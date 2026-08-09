namespace BitLocker.Core;

// Per-drive single-instance support for BitLocker.Unlock.exe. A second launch
// signals the existing unlock window instead of opening a duplicate prompt.
public static class BitLockerUnlockActivation
{
    public static Mutex? TryAcquireUnlockMutex(string drivePath)
    {
        try
        {
            string mutexName = GetUnlockMutexName(drivePath);
            Mutex mutex = new(initiallyOwned: true, name: mutexName, createdNew: out bool createdNew);

            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }

            return mutex;
        }
        catch
        {
            return null;
        }
    }

    public static void SignalExistingUnlockWindow(string drivePath)
    {
        try
        {
            using EventWaitHandle handle = EventWaitHandle.OpenExisting(GetUnlockActivateEventName(drivePath));
            handle.Set();
        }
        catch
        {
        }
    }

    public static string GetUnlockActivateEventName(string drivePath)
    {
        return @"Local\WinPeShell_BitLockerUnlock_Activate_" + BitLockerDrivePath.ToSafeName(drivePath);
    }

    private static string GetUnlockMutexName(string drivePath)
    {
        return @"Local\WinPeShell_BitLockerUnlock_" + BitLockerDrivePath.ToSafeName(drivePath);
    }
}
