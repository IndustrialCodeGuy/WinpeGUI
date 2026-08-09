using Explorer.UI.Icons;
using Shared.Shell.Models;

namespace Explorer.UI.Shell;

public partial class ExplorerShellWindow
{
    private ExplorerImageListManager? _imageListManager;

    private void UpdateCurrentLocationWindowIcon()
    {
        Icon? icon = _presenter.CreateCurrentLocationWindowIcon(GetWindowChromeIconSize());
        SetOwnedWindowIcon(icon);
    }

    internal void RefreshCurrentLocationWindowIcon()
    {
        UpdateCurrentLocationWindowIcon();
    }

    private void SetOwnedWindowIcon(Icon? icon)
    {
        if (icon == null)
            return;

        Icon? oldIcon = _windowIcon;
        _windowIcon = icon;
        Icon = icon;

        oldIcon?.Dispose();
    }

    private static int GetWindowChromeIconSize()
    {
        int width = SystemInformation.SmallIconSize.Width;
        return Math.Max(16, width);
    }

    private void DisposeWindowIcon()
    {
        Icon? icon = _windowIcon;
        _windowIcon = null;

        if (ReferenceEquals(Icon, icon))
            Icon = null;

        icon?.Dispose();
    }

    private void InitializeImageLists()
    {
        _imageListManager ??= new ExplorerImageListManager(
            _iconCache,
            _fileAssociations,
            components,
            _mPx.SmallImageSize);

        _tvNav.ImageList = _imageListManager.TreeImages;
        _lvItems.SmallImageList = _imageListManager.SmallImages;

        _imageListManager.WarmCoreImages();
    }

    private void ApplyImageListMetrics()
    {
        if (_imageListManager is null)
            return;

        bool sizeMatches =
            _imageListManager.TreeImages.ImageSize == _mPx.SmallImageSize &&
            _imageListManager.SmallImages.ImageSize == _mPx.SmallImageSize;

        bool listsAttached =
            ReferenceEquals(_tvNav.ImageList, _imageListManager.TreeImages) &&
            ReferenceEquals(_lvItems.SmallImageList, _imageListManager.SmallImages);

        if (sizeMatches && listsAttached)
            return;

        if (!sizeMatches)
        {
            // TreeView/ListView cache native image-list layout state. When DPI is
            // reduced, especially back to 100%, detach before resizing/rebuilding
            // the ImageList handles so the native controls cannot keep stale
            // expander/line hit-test offsets from the previous DPI.
            _tvNav.ImageList = null;
            _lvItems.SmallImageList = null;

            _imageListManager.ApplyImageSize(_mPx.SmallImageSize);
        }

        if (!ReferenceEquals(_tvNav.ImageList, _imageListManager.TreeImages))
            _tvNav.ImageList = _imageListManager.TreeImages;

        if (!ReferenceEquals(_lvItems.SmallImageList, _imageListManager.SmallImages))
            _lvItems.SmallImageList = _imageListManager.SmallImages;
    }

    private string EnsureThisPcTreeImageKey()
    {
        return _imageListManager!.EnsureThisPcTreeImageKey();
    }

    private string EnsureDriveTreeImageKey(DriveSnapshot drive)
    {
        return _imageListManager!.EnsureDriveTreeImageKey(drive);
    }

    private string EnsureFolderTreeImageKey(bool isVisibleHidden)
    {
        return _imageListManager!.EnsureFolderTreeImageKey(isVisibleHidden);
    }

    private string EnsureListImageKey(ExplorerListRow row)
    {
        return _imageListManager!.EnsureListImageKey(row);
    }

    private string EnsureGhostedTreeImageKey(string sourceImageKey)
    {
        return _imageListManager!.EnsureGhostedTreeImageKey(sourceImageKey);
    }

    private string EnsureGhostedListImageKey(string sourceImageKey)
    {
        return _imageListManager!.EnsureGhostedSmallImageKey(sourceImageKey);
    }

    private static string RemoveCutGhostedImageKeySuffix(string imageKey)
    {
        return ExplorerImageListManager.RemoveCutGhostedImageKeySuffix(imageKey);
    }

    private bool TryEnsurePathSpecificListImageKey(ExplorerListRow row, out string imageKey)
    {
        return _imageListManager!.TryEnsurePathSpecificListImageKey(row, out imageKey);
    }

    private void ClearPathSpecificListImages()
    {
        _imageListManager?.ClearPathSpecificListImages();
    }
}
