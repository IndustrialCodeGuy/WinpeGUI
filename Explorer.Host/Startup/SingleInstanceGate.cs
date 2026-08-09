namespace Explorer.Host.Startup;

internal sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsPrimaryInstance { get; }

    public SingleInstanceGate(string name)
    {
        _mutex = new Mutex(true, name, out bool createdNew);
        IsPrimaryInstance = createdNew;
    }

    public void Dispose()
    {
        try
        {
            if (IsPrimaryInstance)
                _mutex.ReleaseMutex();
        }
        catch
        {
        }

        _mutex.Dispose();
    }
}