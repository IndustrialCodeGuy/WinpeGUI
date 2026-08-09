namespace Shell.Core.Interfaces;

public interface IExplorerWindowRegistry
{
    void Register(IExplorerWindow window);
    void Unregister(string windowId);
    IReadOnlyList<IExplorerWindow> GetAllWindows();
}