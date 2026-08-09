using Explorer.UI.Shell;
using Shell.Core.Interfaces;
using Shell.Core.Models;
using Shell.Core.FileTypes;
using Explorer.UI.Icons;

namespace Explorer.Host;

internal sealed class ExplorerWindowFactory : IExplorerWindowFactory
{
    private readonly IExplorerShellCommands _commands;
    private readonly IExplorerDirectoryService _directoryService;
    private readonly IExplorerCommandService _commandService;
    private readonly ExplorerIconCache _iconCache;
    private readonly IExplorerFileAssociationService _fileAssociations;

    public ExplorerWindowFactory(
        IExplorerShellCommands commands,
        IExplorerDirectoryService directoryService,
        IExplorerCommandService commandService,
        ExplorerIconCache iconCache,
        IExplorerFileAssociationService fileAssociations)
    {
        _commands = commands;
        _directoryService = directoryService;
        _commandService = commandService;
        _iconCache = iconCache;
        _fileAssociations = fileAssociations;
    }

    public IExplorerWindow CreateWindow(ExplorerWindowOptions options)
    {
        return new ExplorerShellWindow(_commands, _directoryService, _commandService, _iconCache, _fileAssociations, options);
    }
}
