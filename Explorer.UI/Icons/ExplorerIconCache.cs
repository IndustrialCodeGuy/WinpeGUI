using Shared.Shell.Models;
using Shared.Shell.Utilities;
using Shell.Core.FileTypes;
using System.Drawing.Imaging;

namespace Explorer.UI.Icons;

public sealed class ExplorerIconCache : IDisposable
{
    // Fixed icon indexes are intentionally centralized here because PE-safe visual
    // tuning may require adjusting specific resources after testing in WinPE.
    private const int ImageresTextDocumentIconIndex = -102;
    private const int ImageresSetupInformationIconIndex = -69;
    private const int ImageresZipIconIndex = -174;
    private const int ImageresExecutableIconIndex = -15;
    private const int ImageresCommandScriptIconIndex = -68;
    private const int ImageresImageIconIndex = -132;
    private const int ImageresDllIconIndex = -67;
    private const int ImageresSystemFileIconIndex = -79;

    private const int Msxml3XslIconIndex = 1;
    private const int PresentationHostDocumentIconIndex = 2;
    private const int ScrobjScriptletIconIndex = 0;

    private const int WScriptVbsScriptIconIndex = 2;
    private const int WScriptJsScriptIconIndex = 3;

    private const int CryptuiSecurityCatalogIconIndex = -3418;
    private const int CryptuiCertificateFileIconIndex = -3410;

    private const int RegeditRegistryIconIndex = 1;

    private static readonly string[] CoreKnownTypeIds =
    [
        ExplorerKnownFileTypeIds.Text,
        ExplorerKnownFileTypeIds.SetupInformation,
        ExplorerKnownFileTypeIds.Catalog,
        ExplorerKnownFileTypeIds.Pkcs7Certificate,
        ExplorerKnownFileTypeIds.CertificateStore,
        ExplorerKnownFileTypeIds.Zip,
        ExplorerKnownFileTypeIds.Xsl,
        ExplorerKnownFileTypeIds.PresentationHostDocument,
        ExplorerKnownFileTypeIds.Scriptlet,
        ExplorerKnownFileTypeIds.Css,
        ExplorerKnownFileTypeIds.ComExecutable,
        ExplorerKnownFileTypeIds.CompressedArchive,
        ExplorerKnownFileTypeIds.Dll,
        ExplorerKnownFileTypeIds.SystemFile,
        ExplorerKnownFileTypeIds.Executable,
        ExplorerKnownFileTypeIds.PowerShellScript,
        ExplorerKnownFileTypeIds.CommandScript,
        ExplorerKnownFileTypeIds.VbsScript,
        ExplorerKnownFileTypeIds.JsScript,
        ExplorerKnownFileTypeIds.Registry,
        ExplorerKnownFileTypeIds.Image
    ];

    private static readonly DriveVisualKind[] CoreDriveVisualKinds = Enum.GetValues<DriveVisualKind>();

    private readonly object _syncRoot = new();
    private readonly Dictionary<ExplorerIconKey, Image> _images = new();
    private bool _disposed;

    internal Image GetImage(ExplorerIconKey key)
    {
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            if (_images.TryGetValue(key, out Image? existing))
                return existing;
        }

        Image created = CreateImage(key);

        lock (_syncRoot)
        {
            if (_images.TryGetValue(key, out Image? existing))
            {
                created.Dispose();
                return existing;
            }

            _images[key] = created;
            return created;
        }
    }

    public void WarmCoreImages(int size)
    {
        ThrowIfDisposed();

        int normalizedSize = ExplorerIconRules.NormalizeSize(size);

        _ = GetImage(new ExplorerIconKey(
            ExplorerIconSourceKind.ThisPc,
            string.Empty,
            normalizedSize,
            false));

        _ = GetImage(new ExplorerIconKey(
            ExplorerIconSourceKind.Folder,
            string.Empty,
            normalizedSize,
            false));

        _ = GetImage(new ExplorerIconKey(
            ExplorerIconSourceKind.Folder,
            string.Empty,
            normalizedSize,
            true));

        _ = GetImage(new ExplorerIconKey(
            ExplorerIconSourceKind.FileNoExtension,
            string.Empty,
            normalizedSize,
            false));

        _ = GetImage(new ExplorerIconKey(
            ExplorerIconSourceKind.FileNoExtension,
            string.Empty,
            normalizedSize,
            true));

        foreach (string knownType in CoreKnownTypeIds)
        {
            _ = GetImage(new ExplorerIconKey(
                ExplorerIconSourceKind.AssociationKnownType,
                knownType,
                normalizedSize,
                false));
        }

        foreach (DriveVisualKind visualKind in CoreDriveVisualKinds)
        {
            _ = GetImage(new ExplorerIconKey(
                ExplorerIconSourceKind.Drive,
                visualKind.ToString(),
                normalizedSize,
                false));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        List<Image> images;

        lock (_syncRoot)
        {
            if (_disposed)
                return;

            _disposed = true;
            images = _images.Values.ToList();
            _images.Clear();
        }

        foreach (Image image in images)
            image.Dispose();
    }

    public static Image CreateUncachedFileSystemItemImage(
    IExplorerFileAssociationService fileAssociations,
    string path,
    bool isDirectory,
    int size,
    bool hidden = false)
    {
        ArgumentNullException.ThrowIfNull(fileAssociations);

        int normalizedSize = ExplorerIconRules.NormalizeSize(size);
        ExplorerIconKey key;

        if (isDirectory)
        {
            key = new ExplorerIconKey(
                ExplorerIconSourceKind.Folder,
                string.Empty,
                normalizedSize,
                hidden);
        }
        else
        {
            string extension = Path.GetExtension(path);

            if (TryCreatePathSpecificIconKey(path, extension, normalizedSize, hidden, out ExplorerIconKey pathSpecificKey))
            {
                key = pathSpecificKey;
            }
            else
            {
                ExplorerFileIconIdentity identity = fileAssociations.ResolveForExtension(extension).IconIdentity;
                key = CreateAssociationIconKey(identity, normalizedSize, hidden);
            }
        }

        using ExplorerIconCache cache = new();
        return cache.CreateUncachedImage(key);
    }

    internal Image CreateUncachedImage(ExplorerIconKey key)
    {
        ThrowIfDisposed();

        if (!key.Hidden)
            return CreateBaseImage(key);

        using Image baseImage = CreateUncachedImage(key.WithoutHidden());
        return CreateGhostedImage(baseImage);
    }

    private static bool TryCreatePathSpecificIconKey(
    string path,
    string? extension,
    int size,
    bool hidden,
    out ExplorerIconKey key)
    {
        key = default;

        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (!ExplorerIconRules.UsesPathSpecificFileIcon(extension))
            return false;

        key = new ExplorerIconKey(
            ExplorerIconSourceKind.PathSpecificFile,
            path,
            size,
            hidden);

        return true;
    }

    private static ExplorerIconKey CreateAssociationIconKey(
        ExplorerFileIconIdentity identity,
        int size,
        bool hidden)
    {
        return identity.Kind switch
        {
            ExplorerFileIconIdentityKind.Handler => new ExplorerIconKey(
                ExplorerIconSourceKind.AssociationHandler,
                ExplorerIconRules.NormalizeIdentity(identity.Value),
                size,
                hidden),

            ExplorerFileIconIdentityKind.KnownType => new ExplorerIconKey(
                ExplorerIconSourceKind.AssociationKnownType,
                ExplorerIconRules.NormalizeIdentity(identity.Value),
                size,
                hidden),

            ExplorerFileIconIdentityKind.FilePath => new ExplorerIconKey(
                ExplorerIconSourceKind.FilePath,
                identity.Value,
                size,
                hidden),

            _ => new ExplorerIconKey(
                ExplorerIconSourceKind.FileNoExtension,
                string.Empty,
                size,
                hidden)
        };
    }

    private Image CreateImage(ExplorerIconKey key)
    {
        if (key.Hidden)
            return CreateHiddenImage(key);

        return CreateBaseImage(key);
    }

    private Image CreateBaseImage(ExplorerIconKey key)
    {
        return key.Kind switch
        {
            ExplorerIconSourceKind.ThisPc => LoadThisPcImage(key.Size),
            ExplorerIconSourceKind.Drive => LoadDriveImage(key.Identity, key.Size),
            ExplorerIconSourceKind.Folder => LoadFolderImage(key.Size),
            ExplorerIconSourceKind.FilePath => LoadFilePathImage(key.Identity, key.Size),
            ExplorerIconSourceKind.PathSpecificFile => LoadFilePathImage(key.Identity, key.Size),
            ExplorerIconSourceKind.FileNoExtension => LoadFileNoExtensionImage(key.Size),
            ExplorerIconSourceKind.AssociationHandler => LoadAssociationHandlerImage(key.Identity, key.Size),
            ExplorerIconSourceKind.AssociationKnownType => LoadAssociationKnownTypeImage(key.Identity, key.Size),
            _ => SystemIcons.WinLogo.ToBitmap()
        };
    }

    private Image CreateHiddenImage(ExplorerIconKey key)
    {
        Image baseImage = GetImage(key.WithoutHidden());
        return CreateGhostedImage(baseImage);
    }

    private static Image LoadThisPcImage(int size)
    {
        string imageResPath = Path.Combine(Environment.SystemDirectory, "imageres.dll");

        return IconUtil.FromFileIconIndex(imageResPath, 104, size)
            ?? IconUtil.FromGenericFolder(size)
            ?? SystemIcons.WinLogo.ToBitmap();
    }

    private static Image LoadDriveImage(string visualKindIdentity, int size)
    {
        DriveVisualKind visualKind = Enum.TryParse(visualKindIdentity, out DriveVisualKind parsed)
            ? parsed
            : DriveVisualKind.Fixed;

        string imageResPath = Path.Combine(Environment.SystemDirectory, "imageres.dll");
        int iconIndex = DriveIconMap.GetImageresIconIndex(visualKind);

        return IconUtil.FromFileIconIndex(imageResPath, iconIndex, size)
            ?? SystemIcons.WinLogo.ToBitmap();
    }

    private static Image LoadFolderImage(int size)
    {
        return IconUtil.FromGenericFolder(size)
            ?? SystemIcons.WinLogo.ToBitmap();
    }

    private static Image LoadAssociationHandlerImage(string handler, int size)
    {
        // Handler icons are for Open With / user-selected applications, not built-in
        // extension type icons. The handler identity should be the selected
        // executable path so a user association shows the icon for the program they
        // chose. Do not resolve by file extension here.
        return LoadExecutableIcon(handler, size)
            ?? LoadFileNoExtensionImage(size);
    }

    private static Image LoadAssociationKnownTypeImage(string knownType, int size)
    {
        // KnownType icons are intentionally fixed WinPeShell icon identities.
        // Do not call SHGetFileInfo for dummy extensions here; in WinPE those
        // associations are unreliable and can return the same generic fallback
        // for unrelated file types.
        Image? image = knownType switch
        {
            ExplorerKnownFileTypeIds.Text => LoadKnownTextDocumentIcon(size),
            ExplorerKnownFileTypeIds.SetupInformation => LoadKnownSetupInformationIcon(size),
            ExplorerKnownFileTypeIds.Catalog => LoadKnownCatalogIcon(size),
            ExplorerKnownFileTypeIds.Pkcs7Certificate => LoadKnownCertificateFileIcon(size),
            ExplorerKnownFileTypeIds.CertificateStore => LoadKnownCertificateFileIcon(size),
            ExplorerKnownFileTypeIds.Zip => LoadKnownZipIcon(size),
            ExplorerKnownFileTypeIds.Xsl => LoadKnownXslIcon(size),
            ExplorerKnownFileTypeIds.PresentationHostDocument => LoadKnownPresentationHostDocumentIcon(size),
            ExplorerKnownFileTypeIds.Scriptlet => LoadKnownScriptletIcon(size),
            ExplorerKnownFileTypeIds.Css => LoadKnownCssIcon(size),
            ExplorerKnownFileTypeIds.ComExecutable => LoadKnownComExecutableIcon(size),
            ExplorerKnownFileTypeIds.CompressedArchive => LoadKnownZipIcon(size),
            ExplorerKnownFileTypeIds.Dll => LoadKnownDllIcon(size),
            ExplorerKnownFileTypeIds.SystemFile => LoadKnownSystemFileIcon(size),
            ExplorerKnownFileTypeIds.Executable => LoadKnownExecutableIcon(size),
            ExplorerKnownFileTypeIds.PowerShellScript => LoadKnownPowerShellScriptIcon(size),
            ExplorerKnownFileTypeIds.CommandScript => LoadKnownCommandScriptIcon(size),
            ExplorerKnownFileTypeIds.VbsScript => LoadKnownVbsScriptIcon(size),
            ExplorerKnownFileTypeIds.JsScript => LoadKnownJsScriptIcon(size),
            ExplorerKnownFileTypeIds.Script => LoadKnownCommandScriptIcon(size),
            ExplorerKnownFileTypeIds.Registry => LoadKnownRegistryIcon(size),
            ExplorerKnownFileTypeIds.Image => LoadKnownImageIcon(size),
            _ => null
        };

        return image ?? LoadFileNoExtensionImage(size);
    }

    private static Image? LoadKnownTextDocumentIcon(int size)
    {
        return LoadFixedSystemIcon(size, SystemIconLibrary.Imageres, ImageresTextDocumentIconIndex);
    }

    private static Image? LoadKnownSetupInformationIcon(int size)
    {
        return LoadFixedSystemIcon(size, SystemIconLibrary.Imageres, ImageresSetupInformationIconIndex);
    }

    private static Image? LoadKnownCatalogIcon(int size)
    {
        return LoadFixedSystemIcon(size, SystemIconLibrary.Cryptui, CryptuiSecurityCatalogIconIndex);
    }

    private static Image? LoadKnownCertificateFileIcon(int size)
    {
        return LoadFixedSystemIcon(size, SystemIconLibrary.Cryptui, CryptuiCertificateFileIconIndex);
    }

    private static Image? LoadKnownZipIcon(int size)
    {
        return LoadFixedSystemIcon(size, SystemIconLibrary.Imageres, ImageresZipIconIndex);
    }

    private static Image? LoadKnownXslIcon(int size)
    {
        return LoadFixedSystem32Icon(size, "msxml3.dll", Msxml3XslIconIndex)
            ?? LoadKnownTextDocumentIcon(size);
    }

    private static Image? LoadKnownPresentationHostDocumentIcon(int size)
    {
        return LoadFixedSystem32Icon(size, "PresentationHost.exe", PresentationHostDocumentIconIndex);
    }

    private static Image? LoadKnownScriptletIcon(int size)
    {
        return LoadFixedSystem32Icon(size, "scrobj.dll", ScrobjScriptletIconIndex)
            ?? LoadKnownTextDocumentIcon(size);
    }

    private static Image? LoadKnownCssIcon(int size)
    {
        return LoadKnownSetupInformationIcon(size);
    }

    private static Image? LoadKnownComExecutableIcon(int size)
    {
        return LoadKnownExecutableIcon(size);
    }

    private static Image? LoadKnownDllIcon(int size)
    {
        return LoadFixedSystemIcon(size, SystemIconLibrary.Imageres, ImageresDllIconIndex);
    }

    private static Image? LoadKnownRegistryIcon(int size)
    {
        return LoadFixedWindowsIcon(size, "regedit.exe", RegeditRegistryIconIndex);
    }

    private static Image? LoadKnownSystemFileIcon(int size)
    {
        return LoadFixedSystemIcon(size, SystemIconLibrary.Imageres, ImageresSystemFileIconIndex);
    }

    private static Image? LoadKnownExecutableIcon(int size)
    {
        return LoadFixedSystemIcon(size, SystemIconLibrary.Imageres, ImageresExecutableIconIndex);
    }

    private static Image? LoadKnownPowerShellScriptIcon(int size)
    {
        return LoadExecutableIcon("%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe", size)
            ?? LoadKnownTextDocumentIcon(size);
    }

    private static Image? LoadKnownCommandScriptIcon(int size)
    {
        return LoadExecutableIcon("%SystemRoot%\\System32\\cmd.exe", size)
            ?? LoadKnownTextDocumentIcon(size);
    }

    private static Image? LoadKnownVbsScriptIcon(int size)
    {
        return LoadFixedSystem32Icon(size, "WScript.exe", WScriptVbsScriptIconIndex)
            ?? LoadKnownTextDocumentIcon(size);
    }

    private static Image? LoadKnownJsScriptIcon(int size)
    {
        return LoadFixedSystem32Icon(size, "WScript.exe", WScriptJsScriptIconIndex)
            ?? LoadKnownTextDocumentIcon(size);
    }

    private static Image? LoadKnownImageIcon(int size)
    {
        return LoadFixedSystemIcon(size, SystemIconLibrary.Imageres, ImageresImageIconIndex);
    }

    private static Image? LoadExecutableIcon(string executablePath, int size)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        string expandedPath = Environment.ExpandEnvironmentVariables(executablePath);

        if (!Path.IsPathRooted(expandedPath))
            expandedPath = Path.Combine(Environment.SystemDirectory, expandedPath);

        if (!File.Exists(expandedPath))
            return null;

        // Used for program-backed icons where the executable itself is the desired
        // Windows-like visual, such as Open With handlers and script host icons.
        return IconUtil.FromFileIconIndex(expandedPath, 0, size);
    }

    private static Image? LoadFixedSystem32Icon(int size, string fileName, int iconIndex)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        string path = Path.Combine(Environment.SystemDirectory, fileName);

        if (!File.Exists(path))
            return null;

        return IconUtil.FromFileIconIndex(path, iconIndex, size);
    }

    private static Image? LoadFixedWindowsIcon(int size, string fileName, int iconIndex)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (string.IsNullOrWhiteSpace(windowsDirectory))
            return null;

        string path = Path.Combine(windowsDirectory, fileName);

        if (!File.Exists(path))
            return null;

        return IconUtil.FromFileIconIndex(path, iconIndex, size);
    }

    private static Image? LoadFixedSystemIcon(int size, SystemIconLibrary library, int iconIndex)
    {
        string fileName = library switch
        {
            SystemIconLibrary.Shell32 => "shell32.dll",
            SystemIconLibrary.Cryptui => "cryptui.dll",
            _ => "imageres.dll"
        };

        string path = Path.Combine(Environment.SystemDirectory, fileName);

        if (!File.Exists(path))
            return null;

        return IconUtil.FromFileIconIndex(path, iconIndex, size);
    }

    private enum SystemIconLibrary
    {
        Imageres,
        Shell32,
        Cryptui
    }

    private static Image LoadFilePathImage(string fullPath, int size)
    {
        string expandedPath = Environment.ExpandEnvironmentVariables(fullPath);

        return LoadPathSpecificFileImage(expandedPath, size)
            ?? IconUtil.FromFileAssociation(expandedPath, size)
            ?? SystemIcons.WinLogo.ToBitmap();
    }

    private static Image? LoadPathSpecificFileImage(string path, int size)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string extension = Path.GetExtension(path);

        if (!ExplorerIconRules.SupportsDirectPathSpecificExtraction(extension))
            return null;

        if (!File.Exists(path))
            return null;

        return IconUtil.FromFileIconIndex(path, 0, size);
    }

    private static Image LoadFileNoExtensionImage(int size)
    {
        return IconUtil.FromGenericFile(size)
            ?? SystemIcons.WinLogo.ToBitmap();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ExplorerIconCache));
    }

    internal static Image CreateGhostedImage(Image source)
    {
        Bitmap ghosted = new(source.Width, source.Height, PixelFormat.Format32bppArgb);
        ghosted.SetResolution(source.HorizontalResolution, source.VerticalResolution);

        using Graphics graphics = Graphics.FromImage(ghosted);
        using ImageAttributes attributes = new();

        ColorMatrix matrix = new(
            new float[][]
            {
                new float[] { 1f, 0f, 0f, 0f, 0f },
                new float[] { 0f, 1f, 0f, 0f, 0f },
                new float[] { 0f, 0f, 1f, 0f, 0f },
                new float[] { 0f, 0f, 0f, 0.60f, 0f },
                new float[] { 0f, 0f, 0f, 0f, 1f }
            });

        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

        graphics.DrawImage(
            source,
            new Rectangle(0, 0, ghosted.Width, ghosted.Height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);

        return ghosted;
    }
}
