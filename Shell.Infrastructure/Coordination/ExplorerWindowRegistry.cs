using Shell.Core.Interfaces;

namespace Shell.Infrastructure.Coordination;

public sealed class ExplorerWindowRegistry : IExplorerWindowRegistry
{
    private readonly List<IExplorerWindow> _windows = [];
    private readonly object _gate = new();

    public void Register(IExplorerWindow window)
    {
        lock (_gate)
        {
            if (_windows.Any(w => w.WindowId == window.WindowId))
                return;

            _windows.Add(window);
        }
    }

    public void Unregister(string windowId)
    {
        lock (_gate)
        {
            _windows.RemoveAll(w => string.Equals(w.WindowId, windowId, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<IExplorerWindow> GetAllWindows()
    {
        lock (_gate)
        {
            return _windows.ToArray();
        }
    }
}
