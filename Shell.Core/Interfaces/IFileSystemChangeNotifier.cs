using Shell.Core.Models;

namespace Shell.Core.Interfaces;

public interface IFileSystemChangeNotifier
{
    void NotifyFileChanged(string parentFolderPath, RefreshReason reason);
    void NotifyFolderChildrenChanged(string parentFolderPath, RefreshReason reason);
    void NotifyFolderRelocated(string oldPath, string newPath, RefreshReason reason);
    void NotifyFolderDeleted(string deletedFolderPath, RefreshReason reason);
}