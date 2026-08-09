using Shell.Core.Models;

namespace Shell.Core.Interfaces;

public interface IExplorerWindowFactory
{
    IExplorerWindow CreateWindow(ExplorerWindowOptions options);
}
