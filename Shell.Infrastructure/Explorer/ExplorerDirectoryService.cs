using Shell.Core.FileTypes;
using Shell.Core.Interfaces;
using Shell.Core.Models;
using Shell.Infrastructure.FileTypes;

namespace Shell.Infrastructure.Explorer;

public sealed class ExplorerDirectoryService : IExplorerDirectoryService
{
    private readonly IExplorerFileAssociationService _fileAssociations;

    private static readonly EnumerationOptions EntryEnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0
    };

    private readonly ExplorerVisibilityOptions _visibility;

    public ExplorerDirectoryService()
        : this(new ExplorerFileAssociationService(), ExplorerVisibilityOptions.CurrentDefault)
    {
    }

    public ExplorerDirectoryService(ExplorerVisibilityOptions visibility)
        : this(new ExplorerFileAssociationService(), visibility)
    {
    }

    public ExplorerDirectoryService(
        IExplorerFileAssociationService fileAssociations,
        ExplorerVisibilityOptions visibility)
    {
        _fileAssociations = fileAssociations ?? throw new ArgumentNullException(nameof(fileAssociations));
        _visibility = visibility;
    }

    public Task<ExplorerDirectoryListing> LoadDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be blank.", nameof(path));

        string normalizedPath = Path.GetFullPath(path);

        return Task.Run(() => LoadDirectory(normalizedPath, cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyList<ExplorerDirectoryItem>> LoadChildDirectoriesAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be blank.", nameof(path));

        string normalizedPath = Path.GetFullPath(path);

        return Task.Run<IReadOnlyList<ExplorerDirectoryItem>>(
            () => LoadDirectories(normalizedPath, cancellationToken),
            cancellationToken);
    }

    public IReadOnlyList<ExplorerDirectoryItem> LoadChildDirectories(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be blank.", nameof(path));

        string normalizedPath = Path.GetFullPath(path);
        return LoadDirectories(normalizedPath, CancellationToken.None);
    }

    private const string FolderTypeText = "Folder";

    private ExplorerDirectoryListing LoadDirectory(string path, CancellationToken cancellationToken)
    {
        List<ExplorerDirectoryItem> items = [];
        Dictionary<string, string> typeTextByExtension = new(StringComparer.OrdinalIgnoreCase);

        DirectoryInfo root = new(path);

        foreach (FileSystemInfo entry in root.EnumerateFileSystemInfos("*", EntryEnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                FileAttributes attributes = entry.Attributes;

                if (!ExplorerVisibilityPolicy.ShouldInclude(attributes, _visibility))
                    continue;

                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                bool isVisibleHidden = ExplorerVisibilityPolicy.IsVisibleHidden(attributes, _visibility);

                if (isDirectory)
                {
                    items.Add(new ExplorerDirectoryItem
                    {
                        Name = entry.Name,
                        FullPath = entry.FullName,
                        IsDirectory = true,
                        IsVisibleHidden = isVisibleHidden,
                        TypeText = FolderTypeText,
                        ModifiedLocalTime = entry.LastWriteTime
                    });

                    continue;
                }

                FileInfo file = entry as FileInfo ?? new FileInfo(entry.FullName);
                string extension = file.Extension;

                items.Add(new ExplorerDirectoryItem
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsDirectory = false,
                    IsVisibleHidden = isVisibleHidden,
                    TypeText = GetCachedTypeText(typeTextByExtension, extension),
                    Extension = extension,
                    SizeBytes = file.Length,
                    ModifiedLocalTime = file.LastWriteTime
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        return new ExplorerDirectoryListing
        {
            DirectoryPath = path,
            Items = items
        };
    }

    private IReadOnlyList<ExplorerDirectoryItem> LoadDirectories(string path, CancellationToken cancellationToken)
    {
        List<ExplorerDirectoryItem> items = [];
        DirectoryInfo root = new(path);

        try
        {
            foreach (DirectoryInfo directory in root.EnumerateDirectories("*", EntryEnumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    FileAttributes attributes = directory.Attributes;

                    if (!ExplorerVisibilityPolicy.ShouldInclude(attributes, _visibility))
                        continue;

                    items.Add(new ExplorerDirectoryItem
                    {
                        Name = directory.Name,
                        FullPath = directory.FullName,
                        IsDirectory = true,
                        IsVisibleHidden = ExplorerVisibilityPolicy.IsVisibleHidden(attributes, _visibility),
                        TypeText = FolderTypeText
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        items.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

        return items;
    }

    private string GetCachedTypeText(Dictionary<string, string> cache, string? extension)
    {
        string normalizedExtension = string.IsNullOrWhiteSpace(extension) ? string.Empty : extension;

        if (cache.TryGetValue(normalizedExtension, out string? typeText))
            return typeText;

        typeText = _fileAssociations.ResolveForExtension(normalizedExtension).DisplayName;
        cache[normalizedExtension] = typeText;
        return typeText;
    }
}