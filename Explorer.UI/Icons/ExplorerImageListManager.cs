using Explorer.UI.Shell;
using Shared.Shell.Models;
using Shell.Core.FileTypes;
using System.ComponentModel;

namespace Explorer.UI.Icons;

internal sealed class ExplorerImageListManager
{
    private readonly ExplorerIconCache _iconCache;
    private readonly ExplorerIconPolicy _iconPolicy;
    private readonly HashSet<string> _treeImageKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _smallImageKeys = new(StringComparer.OrdinalIgnoreCase);
    private const string CutGhostedImageKeySuffix = ":cutghost";
    private static readonly DriveVisualKind[] CoreDriveVisualKinds = Enum.GetValues<DriveVisualKind>();
    private readonly HashSet<string> _smallPathSpecificImageKeys = new(StringComparer.OrdinalIgnoreCase);

    public ExplorerImageListManager(
        ExplorerIconCache iconCache,
        IExplorerFileAssociationService fileAssociations,
        IContainer? container,
        Size smallImageSize)
    {
        _iconCache = iconCache;
        _iconPolicy = new ExplorerIconPolicy(fileAssociations);

        TreeImages = new ImageList(container)
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize = smallImageSize
        };

        SmallImages = new ImageList(container)
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize = smallImageSize
        };
    }

    public ImageList TreeImages { get; }

    public ImageList SmallImages { get; }

    public bool ApplyImageSize(Size smallImageSize)
    {
        if (TreeImages.ImageSize == smallImageSize &&
            SmallImages.ImageSize == smallImageSize)
        {
            return false;
        }

        TreeImages.Images.Clear();
        SmallImages.Images.Clear();

        _treeImageKeys.Clear();
        _smallImageKeys.Clear();
        _smallPathSpecificImageKeys.Clear();

        TreeImages.ImageSize = smallImageSize;
        SmallImages.ImageSize = smallImageSize;

        WarmCoreImages();
        return true;
    }

    public string EnsureThisPcTreeImageKey()
    {
        ExplorerIconKey key = ExplorerIconPolicy.GetThisPcIconKey(TreeImages.ImageSize.Width);
        return EnsureImage(TreeImages, _treeImageKeys, key);
    }

    public string EnsureDriveTreeImageKey(DriveSnapshot drive)
    {
        ExplorerIconKey key = ExplorerIconPolicy.GetDriveIconKey(drive.VisualKind, TreeImages.ImageSize.Width);
        return EnsureImage(TreeImages, _treeImageKeys, key);
    }

    public string EnsureFolderTreeImageKey(bool isVisibleHidden)
    {
        ExplorerIconKey key = ExplorerIconPolicy.GetFolderIconKey(TreeImages.ImageSize.Width, isVisibleHidden);
        return EnsureImage(TreeImages, _treeImageKeys, key);
    }

    public string EnsureListImageKey(ExplorerListRow row)
    {
        ExplorerIconKey key = _iconPolicy.GetListIconKey(row, SmallImages.ImageSize.Width);
        return EnsureImage(SmallImages, _smallImageKeys, key);
    }

    public string EnsureGhostedTreeImageKey(string sourceImageKey)
    {
        return EnsureGhostedImageKey(TreeImages, _treeImageKeys, sourceImageKey);
    }

    public string EnsureGhostedSmallImageKey(string sourceImageKey)
    {
        string normalizedSourceKey = RemoveCutGhostedImageKeySuffix(sourceImageKey);
        string ghostedImageKey = EnsureGhostedImageKey(SmallImages, _smallImageKeys, normalizedSourceKey);

        if (!string.Equals(ghostedImageKey, normalizedSourceKey, StringComparison.OrdinalIgnoreCase) &&
            _smallPathSpecificImageKeys.Contains(normalizedSourceKey))
        {
            _smallPathSpecificImageKeys.Add(ghostedImageKey);
        }

        return ghostedImageKey;
    }

    public static string RemoveCutGhostedImageKeySuffix(string imageKey)
    {
        if (string.IsNullOrEmpty(imageKey))
            return string.Empty;

        return imageKey.EndsWith(CutGhostedImageKeySuffix, StringComparison.OrdinalIgnoreCase)
             ? imageKey[..^CutGhostedImageKeySuffix.Length]
             : imageKey;
    }

    private static bool IsAlreadyGhostedImageKey(string imageKey)
    {
        return imageKey.EndsWith(":hidden", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCutGhostedImageKey(string sourceImageKey)
    {
        return RemoveCutGhostedImageKeySuffix(sourceImageKey) + CutGhostedImageKeySuffix;
    }

    private string EnsureGhostedImageKey(
        ImageList imageList,
        HashSet<string> imageKeys,
        string sourceImageKey)
    {
        sourceImageKey = RemoveCutGhostedImageKeySuffix(sourceImageKey);

        if (string.IsNullOrEmpty(sourceImageKey) || IsAlreadyGhostedImageKey(sourceImageKey))
            return sourceImageKey;

        string ghostedImageKey = BuildCutGhostedImageKey(sourceImageKey);
        if (imageKeys.Contains(ghostedImageKey) && imageList.Images.ContainsKey(ghostedImageKey))
            return ghostedImageKey;

        int sourceIndex = imageList.Images.IndexOfKey(sourceImageKey);
        if (sourceIndex < 0)
            return sourceImageKey;

        using Image ghostedImage = ExplorerIconCache.CreateGhostedImage(imageList.Images[sourceIndex]);
        imageList.Images.Add(ghostedImageKey, ghostedImage);
        imageKeys.Add(ghostedImageKey);

        return ghostedImageKey;
    }


    private string EnsureImage(
        ImageList imageList,
        HashSet<string> imageKeys,
        ExplorerIconKey key)
    {
        string imageKey = key.ImageListKey;

        if (imageKeys.Contains(imageKey))
            return imageKey;

        Image image = _iconCache.GetImage(key);

        imageList.Images.Add(imageKey, image);
        imageKeys.Add(imageKey);

        return imageKey;
    }

    public bool TryEnsurePathSpecificListImageKey(ExplorerListRow row, out string imageKey)
    {
        imageKey = string.Empty;

        if (!ExplorerIconPolicy.TryGetPathSpecificListIconKey(row, SmallImages.ImageSize.Width, out ExplorerIconKey key))
            return false;

        imageKey = key.ImageListKey;

        if (_smallPathSpecificImageKeys.Contains(imageKey))
            return true;

        using Image image = _iconCache.CreateUncachedImage(key);

        SmallImages.Images.Add(imageKey, image);
        _smallPathSpecificImageKeys.Add(imageKey);

        return true;
    }

    public void WarmCoreImages()
    {
        EnsureImage(TreeImages, _treeImageKeys, ExplorerIconPolicy.GetThisPcIconKey(TreeImages.ImageSize.Width));
        EnsureImage(TreeImages, _treeImageKeys, ExplorerIconPolicy.GetFolderIconKey(TreeImages.ImageSize.Width, false));
        EnsureImage(TreeImages, _treeImageKeys, ExplorerIconPolicy.GetFolderIconKey(TreeImages.ImageSize.Width, true));

        EnsureImage(SmallImages, _smallImageKeys, ExplorerIconPolicy.GetThisPcIconKey(SmallImages.ImageSize.Width));
        EnsureImage(SmallImages, _smallImageKeys, ExplorerIconPolicy.GetFolderIconKey(SmallImages.ImageSize.Width, false));
        EnsureImage(SmallImages, _smallImageKeys, ExplorerIconPolicy.GetFolderIconKey(SmallImages.ImageSize.Width, true));

        EnsureImage(
            SmallImages,
            _smallImageKeys,
            new ExplorerIconKey(
                ExplorerIconSourceKind.FileNoExtension,
                string.Empty,
                SmallImages.ImageSize.Width,
                false));

        EnsureImage(
            SmallImages,
            _smallImageKeys,
            new ExplorerIconKey(
                ExplorerIconSourceKind.FileNoExtension,
                string.Empty,
                SmallImages.ImageSize.Width,
                true));

        foreach (DriveVisualKind visualKind in CoreDriveVisualKinds)
        {
            EnsureImage(
                TreeImages,
                _treeImageKeys,
                ExplorerIconPolicy.GetDriveIconKey(visualKind, TreeImages.ImageSize.Width));

            EnsureImage(
                SmallImages,
                _smallImageKeys,
                ExplorerIconPolicy.GetDriveIconKey(visualKind, SmallImages.ImageSize.Width));
        }
    }

    public void ClearPathSpecificListImages()
    {
        if (_smallPathSpecificImageKeys.Count == 0)
            return;

        foreach (string imageListKey in _smallPathSpecificImageKeys)
        {
            SmallImages.Images.RemoveByKey(imageListKey);
            _smallImageKeys.Remove(imageListKey);
        }

        _smallPathSpecificImageKeys.Clear();
    }
}