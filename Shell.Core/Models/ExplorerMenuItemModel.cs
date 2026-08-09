namespace Shell.Core.Models;

public sealed class ExplorerMenuItemModel
{
    public ExplorerCommandId? CommandId { get; init; }
    public string? CommandArgument { get; init; }
    public string Text { get; init; } = string.Empty;
    public bool Visible { get; init; } = true;
    public bool Enabled { get; init; } = true;
    public bool IsSeparator { get; init; }
    public IReadOnlyList<ExplorerMenuItemModel> Children { get; init; } = Array.Empty<ExplorerMenuItemModel>();

    public static ExplorerMenuItemModel Separator() =>
        new()
        {
            IsSeparator = true
        };

    public static ExplorerMenuItemModel Command(
        ExplorerCommandId commandId,
        string text,
        bool enabled = true,
        bool visible = true,
        string? commandArgument = null) =>
        new()
        {
            CommandId = commandId,
            CommandArgument = commandArgument,
            Text = text,
            Enabled = enabled,
            Visible = visible
        };

    public static ExplorerMenuItemModel Submenu(
        string text,
        IReadOnlyList<ExplorerMenuItemModel> children) =>
        new()
        {
            Text = text,
            Children = children,
            Visible = children.Count > 0
        };
}