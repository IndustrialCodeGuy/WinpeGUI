using Shell.Core.Models;

namespace Shell.Core.Interfaces;

public interface IExplorerDirectoryService
{
    Task<ExplorerDirectoryListing> LoadDirectoryAsync(string path, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExplorerDirectoryItem>> LoadChildDirectoriesAsync(string path, CancellationToken cancellationToken);
    IReadOnlyList<ExplorerDirectoryItem> LoadChildDirectories(string path);
}