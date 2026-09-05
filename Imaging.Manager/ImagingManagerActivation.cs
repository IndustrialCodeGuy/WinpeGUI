namespace Imaging.Manager;

internal static class ImagingManagerActivation
{
    private const string ManagerMutexName = @"Local\WinPEGUI.ImagingManager";
    private const string ManagerActivateEventName = @"Local\WinPEGUI.ImagingManager.Activate";

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

    public static EventWaitHandle CreateManagerActivateEvent() =>
        new(initialState: false, mode: EventResetMode.AutoReset, name: ManagerActivateEventName);

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
