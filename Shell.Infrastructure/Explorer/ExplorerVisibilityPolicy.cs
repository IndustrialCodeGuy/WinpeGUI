using Shell.Core.Models;

namespace Shell.Infrastructure.Explorer;

internal static class ExplorerVisibilityPolicy
{
    public static bool ShouldInclude(FileAttributes attributes, ExplorerVisibilityOptions options)
    {
        bool isHidden = (attributes & FileAttributes.Hidden) != 0;
        bool isSystem = (attributes & FileAttributes.System) != 0;
        bool isSuperHidden = isHidden && isSystem;

        if (isSuperHidden && !options.ShowSuperHidden)
            return false;

        if (isSystem && !options.ShowSystem)
            return false;

        if (isHidden && !options.ShowHidden)
            return false;

        return true;
    }

    public static bool IsVisibleHidden(FileAttributes attributes, ExplorerVisibilityOptions options)
    {
        bool isHidden = (attributes & FileAttributes.Hidden) != 0;
        return isHidden && ShouldInclude(attributes, options);
    }
}