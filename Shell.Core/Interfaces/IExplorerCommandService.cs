using Shell.Core.Models;

namespace Shell.Core.Interfaces;

public interface IExplorerCommandService
{
    IReadOnlyList<ExplorerMenuItemModel> BuildContextMenu(ExplorerCommandContext context);

    bool TryExecute(
        ExplorerCommandId commandId,
        ExplorerCommandContext context,
        string? commandArgument = null);
}