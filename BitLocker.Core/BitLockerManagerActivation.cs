namespace BitLocker.Core;

// Process-wide single-instance support for BitLocker.Manager.exe. A second
// launch signals the existing manager window instead of opening a duplicate.
public static class BitLockerManagerActivation
{
    private const string ManagerMutexName = @"Local\WinPeShell_BitLockerManager";
    private const string ManagerActivateEventName = @"Local\WinPeShell_BitLockerManager_Activate";

    public static Mutex? TryAcquireManagerMutex()
    {
        try
        {
            Mutex mutex = new(initiallyOwned: true, name: ManagerMutexName, createdNew: out bool createdNew);

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

    public static EventWaitHandle CreateManagerActivateEvent()
    {
        return new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ManagerActivateEventName);
    }

    public static void SignalExistingManager()
    {
        try
        {
            using EventWaitHandle handle = EventWaitHandle.OpenExisting(ManagerActivateEventName);
            handle.Set();
        }
        catch
        {
        }
    }
}