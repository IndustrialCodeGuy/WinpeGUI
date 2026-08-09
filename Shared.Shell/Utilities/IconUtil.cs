using Shared.Shell.Interop;
using System.Runtime.InteropServices;

namespace Shared.Shell.Utilities
{
    // Low-level icon extraction helpers used by the shared shell caches.
    // Extracted HICON handles owned by this class are destroyed after conversion;
    // window-owned handles are copied before conversion. Returned bitmaps are
    // normalized to 96 DPI and the requested pixel size for stable WinForms layout.

    public static class IconUtil
    {
        public static Icon? FromFileIconIndexIcon(string filePath, int index, int size)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            int targetSize = NormalizeIconSize(size);
            IntPtr hIcon = ExtractExactIcon(filePath, index, targetSize);

            if (hIcon == IntPtr.Zero)
                hIcon = ExtractLegacyBestIcon(filePath, index, targetSize);

            if (hIcon == IntPtr.Zero)
                return null;

            try
            {
                using Icon tmp = Icon.FromHandle(hIcon);
                return (Icon)tmp.Clone();
            }
            finally
            {
                User32.DestroyIcon(hIcon);
            }
        }

        // For exe/dll/ico with an explicit icon index (imageres.dll, shell32.dll, etc.)
        public static Image? FromFileIconIndex(string filePath, int index, int size)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            int targetSize = NormalizeIconSize(size);
            IntPtr hIcon = ExtractExactIcon(filePath, index, targetSize);

            if (hIcon == IntPtr.Zero)
                hIcon = ExtractLegacyBestIcon(filePath, index, targetSize);

            if (hIcon == IntPtr.Zero)
                return null;

            try
            {
                return HIconToBitmapBorrowed(hIcon, targetSize);
            }
            finally
            {
                User32.DestroyIcon(hIcon);
            }
        }

        // For arbitrary file types (ps1/cmd/bat/txt/etc.) -> associated icon
        // For real file paths: prefer shell system image list, then fall back to SHGetFileInfo.
        public static Image? FromFileAssociation(string path, int size)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            int targetSize = NormalizeIconSize(size);
            Image? shellImage = FromShellSystemImageList(path, 0, 0, targetSize);

            if (shellImage != null)
                return shellImage;

            IntPtr hSmall = IntPtr.Zero;
            IntPtr hLarge = IntPtr.Zero;

            try
            {
                uint cb = (uint)Marshal.SizeOf(typeof(SHFILEINFO));
                bool wantLarge = targetSize > 16;

                // 1) Preferred size first
                {
                    SHFILEINFO sfi;
                    uint flags = SHGFI_ICON | (wantLarge ? SHGFI_LARGEICON : SHGFI_SMALLICON);
                    IntPtr res = SHGetFileInfo(path, 0, out sfi, cb, flags);
                    if (res != IntPtr.Zero && sfi.hIcon != IntPtr.Zero)
                    {
                        if (wantLarge) hLarge = sfi.hIcon;
                        else hSmall = sfi.hIcon;
                    }
                }

                // 2) If missing, try the other size
                if ((wantLarge && hLarge == IntPtr.Zero) || (!wantLarge && hSmall == IntPtr.Zero))
                {
                    SHFILEINFO sfi;
                    uint flags = SHGFI_ICON | (wantLarge ? SHGFI_SMALLICON : SHGFI_LARGEICON);
                    IntPtr res = SHGetFileInfo(path, 0, out sfi, cb, flags);
                    if (res != IntPtr.Zero && sfi.hIcon != IntPtr.Zero)
                    {
                        if (wantLarge) hSmall = sfi.hIcon;
                        else hLarge = sfi.hIcon;
                    }
                }

                IntPtr hPick = PickBestHandle(hSmall, hLarge, targetSize);
                if (hPick == IntPtr.Zero)
                    return null;

                return HIconToBitmapBorrowed(hPick, targetSize);
            }
            finally
            {
                if (hSmall != IntPtr.Zero) User32.DestroyIcon(hSmall);
                if (hLarge != IntPtr.Zero) User32.DestroyIcon(hLarge);
            }
        }

        public static Image? FromGenericFile(int size)
        {
            int targetSize = NormalizeIconSize(size);
            const string dummyPath = "dummy";

            Image? shellImage = FromShellSystemImageList(
                dummyPath,
                FILE_ATTRIBUTE_NORMAL,
                SHGFI_USEFILEATTRIBUTES,
                targetSize);

            if (shellImage != null)
                return shellImage;

            IntPtr hSmall = IntPtr.Zero;
            IntPtr hLarge = IntPtr.Zero;

            try
            {
                uint cb = (uint)Marshal.SizeOf(typeof(SHFILEINFO));
                bool wantLarge = targetSize > 16;
                uint baseFlags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES;

                {
                    SHFILEINFO sfi;
                    uint flags = baseFlags | (wantLarge ? SHGFI_LARGEICON : SHGFI_SMALLICON);

                    IntPtr res = SHGetFileInfo(
                        dummyPath,
                        FILE_ATTRIBUTE_NORMAL,
                        out sfi,
                        cb,
                        flags);

                    if (res != IntPtr.Zero && sfi.hIcon != IntPtr.Zero)
                    {
                        if (wantLarge)
                            hLarge = sfi.hIcon;
                        else
                            hSmall = sfi.hIcon;
                    }
                }

                if ((wantLarge && hLarge == IntPtr.Zero) || (!wantLarge && hSmall == IntPtr.Zero))
                {
                    SHFILEINFO sfi;
                    uint flags = baseFlags | (wantLarge ? SHGFI_SMALLICON : SHGFI_LARGEICON);

                    IntPtr res = SHGetFileInfo(
                        dummyPath,
                        FILE_ATTRIBUTE_NORMAL,
                        out sfi,
                        cb,
                        flags);

                    if (res != IntPtr.Zero && sfi.hIcon != IntPtr.Zero)
                    {
                        if (wantLarge)
                            hSmall = sfi.hIcon;
                        else
                            hLarge = sfi.hIcon;
                    }
                }

                IntPtr hPick = PickBestHandle(hSmall, hLarge, targetSize);
                if (hPick == IntPtr.Zero)
                    return null;

                return HIconToBitmapBorrowed(hPick, targetSize);
            }
            finally
            {
                if (hSmall != IntPtr.Zero)
                    User32.DestroyIcon(hSmall);

                if (hLarge != IntPtr.Zero)
                    User32.DestroyIcon(hLarge);
            }
        }

        public static Image? FromGenericFileAssociation(string extension, int size)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return null;

            int targetSize = NormalizeIconSize(size);

            string normalizedExtension = extension.Trim();
            normalizedExtension = normalizedExtension.StartsWith('.')
                ? normalizedExtension
                : "." + normalizedExtension;

            string dummyPath = "dummy" + normalizedExtension;

            Image? shellImage = FromShellSystemImageList(
                dummyPath,
                FILE_ATTRIBUTE_NORMAL,
                SHGFI_USEFILEATTRIBUTES,
                targetSize);

            if (shellImage != null)
                return shellImage;

            IntPtr hSmall = IntPtr.Zero;
            IntPtr hLarge = IntPtr.Zero;

            try
            {
                uint cb = (uint)Marshal.SizeOf(typeof(SHFILEINFO));
                bool wantLarge = targetSize > 16;
                uint baseFlags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES;

                {
                    SHFILEINFO sfi;
                    uint flags = baseFlags | (wantLarge ? SHGFI_LARGEICON : SHGFI_SMALLICON);

                    IntPtr res = SHGetFileInfo(
                        dummyPath,
                        FILE_ATTRIBUTE_NORMAL,
                        out sfi,
                        cb,
                        flags);

                    if (res != IntPtr.Zero && sfi.hIcon != IntPtr.Zero)
                    {
                        if (wantLarge)
                            hLarge = sfi.hIcon;
                        else
                            hSmall = sfi.hIcon;
                    }
                }

                if ((wantLarge && hLarge == IntPtr.Zero) || (!wantLarge && hSmall == IntPtr.Zero))
                {
                    SHFILEINFO sfi;
                    uint flags = baseFlags | (wantLarge ? SHGFI_SMALLICON : SHGFI_LARGEICON);

                    IntPtr res = SHGetFileInfo(
                        dummyPath,
                        FILE_ATTRIBUTE_NORMAL,
                        out sfi,
                        cb,
                        flags);

                    if (res != IntPtr.Zero && sfi.hIcon != IntPtr.Zero)
                    {
                        if (wantLarge)
                            hSmall = sfi.hIcon;
                        else
                            hLarge = sfi.hIcon;
                    }
                }

                IntPtr hPick = PickBestHandle(hSmall, hLarge, targetSize);
                if (hPick == IntPtr.Zero)
                    return null;

                return HIconToBitmapBorrowed(hPick, targetSize);
            }
            finally
            {
                if (hSmall != IntPtr.Zero)
                    User32.DestroyIcon(hSmall);

                if (hLarge != IntPtr.Zero)
                    User32.DestroyIcon(hLarge);
            }
        }

        public static Image? FromGenericFolder(int size)
        {
            int targetSize = NormalizeIconSize(size);
            const string dummyFolderPath = @"C:\Folder";

            Image? shellImage = FromShellSystemImageList(
                dummyFolderPath,
                FILE_ATTRIBUTE_DIRECTORY,
                SHGFI_USEFILEATTRIBUTES,
                targetSize);

            if (shellImage != null)
                return shellImage;

            IntPtr hSmall = IntPtr.Zero;
            IntPtr hLarge = IntPtr.Zero;

            try
            {
                uint cb = (uint)Marshal.SizeOf(typeof(SHFILEINFO));
                bool wantLarge = targetSize > 16;
                uint baseFlags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES;

                // Preferred size first
                {
                    SHFILEINFO sfi;
                    uint flags = baseFlags | (wantLarge ? SHGFI_LARGEICON : SHGFI_SMALLICON);
                    IntPtr res = SHGetFileInfo(
                        dummyFolderPath,
                        FILE_ATTRIBUTE_DIRECTORY,
                        out sfi,
                        cb,
                        flags);

                    if (res != IntPtr.Zero && sfi.hIcon != IntPtr.Zero)
                    {
                        if (wantLarge) hLarge = sfi.hIcon;
                        else hSmall = sfi.hIcon;
                    }
                }

                // Fallback to the other size
                if ((wantLarge && hLarge == IntPtr.Zero) || (!wantLarge && hSmall == IntPtr.Zero))
                {
                    SHFILEINFO sfi;
                    uint flags = baseFlags | (wantLarge ? SHGFI_SMALLICON : SHGFI_LARGEICON);
                    IntPtr res = SHGetFileInfo(
                        dummyFolderPath,
                        FILE_ATTRIBUTE_DIRECTORY,
                        out sfi,
                        cb,
                        flags);

                    if (res != IntPtr.Zero && sfi.hIcon != IntPtr.Zero)
                    {
                        if (wantLarge) hSmall = sfi.hIcon;
                        else hLarge = sfi.hIcon;
                    }
                }

                IntPtr hPick = PickBestHandle(hSmall, hLarge, targetSize);
                if (hPick == IntPtr.Zero)
                    return null;

                return HIconToBitmapBorrowed(hPick, targetSize);
            }
            finally
            {
                if (hSmall != IntPtr.Zero) User32.DestroyIcon(hSmall);
                if (hLarge != IntPtr.Zero) User32.DestroyIcon(hLarge);
            }
        }


        // Convert a window/class-owned HICON by copying it first.
        public static Image? FromWindowIconHandle(IntPtr hIcon, int size)
        {
            if (hIcon == IntPtr.Zero)
                return null;

            int targetSize = NormalizeIconSize(size);

            IntPtr hCopy = User32.CopyIcon(hIcon);
            if (hCopy == IntPtr.Zero)
                return null;

            try
            {
                return HIconToBitmapBorrowed(hCopy, targetSize);
            }
            finally
            {
                User32.DestroyIcon(hCopy);
            }
        }

        // Pick the best available small/large window icon and convert it.
        public static Image? FromWindowIconHandles(IntPtr hSmall, IntPtr hLarge, int size)
        {
            int targetSize = NormalizeIconSize(size);

            IntPtr hPick = PickBestHandle(hSmall, hLarge, targetSize);
            if (hPick == IntPtr.Zero)
                return null;

            return FromWindowIconHandle(hPick, targetSize);
        }

        private static IntPtr ExtractExactIcon(string filePath, int index, int size)
        {
            int extractSize = SelectIconResourceSize(size);

            IntPtr[] icons = new IntPtr[1];
            uint[] iconIds = new uint[1];

            try
            {
                uint extracted = PrivateExtractIcons(
                    filePath,
                    index,
                    extractSize,
                    extractSize,
                    icons,
                    iconIds,
                    1,
                    0);

                if (extracted == 0 || icons[0] == IntPtr.Zero)
                    return IntPtr.Zero;

                IntPtr hIcon = icons[0];
                icons[0] = IntPtr.Zero;
                return hIcon;
            }
            catch
            {
                return IntPtr.Zero;
            }
            finally
            {
                if (icons[0] != IntPtr.Zero)
                    User32.DestroyIcon(icons[0]);
            }
        }

        private static int SelectIconResourceSize(int targetSize)
        {
            targetSize = NormalizeIconSize(targetSize);

            foreach (int resourceSize in StandardIconResourceSizes)
            {
                if (targetSize <= resourceSize)
                    return resourceSize;
            }

            return targetSize;
        }

        private static IntPtr ExtractLegacyBestIcon(string filePath, int index, int size)
        {
            IntPtr[] large = new IntPtr[1];
            IntPtr[] small = new IntPtr[1];

            try
            {
                uint count = ExtractIconEx(filePath, index, large, small, 1);
                if (count == 0)
                    return IntPtr.Zero;

                IntPtr hIcon = PickBestHandle(small[0], large[0], size);
                if (hIcon == IntPtr.Zero)
                    return IntPtr.Zero;

                return User32.CopyIcon(hIcon);
            }
            finally
            {
                if (large[0] != IntPtr.Zero)
                    User32.DestroyIcon(large[0]);

                if (small[0] != IntPtr.Zero)
                    User32.DestroyIcon(small[0]);
            }
        }

        private static int NormalizeIconSize(int size)
        {
            return size > 0 ? size : 16;
        }

        // One rule for everything:
        // - request <= 16 => prefer small
        // - request > 16  => prefer large
        // - if preferred is missing, use the other
        // - if both missing, return zero
        private static IntPtr PickBestHandle(IntPtr small, IntPtr large, int size)
        {
            if (size <= 16)
                return small != IntPtr.Zero ? small : (large != IntPtr.Zero ? large : IntPtr.Zero);
            else
                return large != IntPtr.Zero ? large : (small != IntPtr.Zero ? small : IntPtr.Zero);
        }

        // Does not destroy hIcon; the caller remains responsible for handle lifetime.
        private static Image? HIconToBitmapBorrowed(IntPtr hIcon, int size)
        {
            if (hIcon == IntPtr.Zero)
                return null;

            int targetSize = NormalizeIconSize(size);

            try
            {
                using Icon src = Icon.FromHandle(hIcon);
                Bitmap bmp = src.ToBitmap();
                bmp.SetResolution(96f, 96f);

                if (bmp.Width == targetSize && bmp.Height == targetSize)
                    return bmp;

                Bitmap scaled = new(
                    targetSize,
                    targetSize,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                scaled.SetResolution(96f, 96f);

                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                    g.DrawImage(bmp, new Rectangle(0, 0, targetSize, targetSize));
                }

                bmp.Dispose();
                return scaled;
            }
            catch
            {
                return null;
            }
        }

        private static Image? FromShellSystemImageList(string path, uint attributes, uint extraFlags, int size)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            int targetSize = NormalizeIconSize(size);
            int iconIndex = GetShellSystemImageIndex(path, attributes, extraFlags);

            if (iconIndex < 0)
                return null;

            Guid iid = new("46EB5926-582E-4017-9FDF-E8998DAA0950");
            IImageList? imageList = null;
            IntPtr hIcon = IntPtr.Zero;

            try
            {
                int hr = SHGetImageList(GetShellImageListKind(targetSize), ref iid, out imageList);
                if (hr != 0 || imageList == null)
                    return null;

                hr = imageList.GetIcon(iconIndex, ILD_TRANSPARENT, out hIcon);
                if (hr != 0 || hIcon == IntPtr.Zero)
                    return null;

                return HIconToBitmapBorrowed(hIcon, targetSize);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hIcon != IntPtr.Zero)
                    User32.DestroyIcon(hIcon);

                if (imageList != null)
                    Marshal.ReleaseComObject(imageList);
            }
        }

        private static int GetShellSystemImageIndex(string path, uint attributes, uint extraFlags)
        {
            try
            {
                uint cb = (uint)Marshal.SizeOf(typeof(SHFILEINFO));
                uint flags = SHGFI_SYSICONINDEX | extraFlags;

                IntPtr res = SHGetFileInfo(
                    path,
                    attributes,
                    out SHFILEINFO sfi,
                    cb,
                    flags);

                return res == IntPtr.Zero ? -1 : sfi.iIcon;
            }
            catch
            {
                return -1;
            }
        }

        private static int GetShellImageListKind(int size)
        {
            size = NormalizeIconSize(size);

            if (size <= 16)
                return SHIL_SMALL;

            if (size <= 32)
                return SHIL_LARGE;

            if (size <= 48)
                return SHIL_EXTRALARGE;

            return SHIL_JUMBO;
        }

        [DllImport("shell32.dll", EntryPoint = "#727")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList? ppv);

        [ComImport]
        [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig]
            int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);

            [PreserveSig]
            int ReplaceIcon(int i, IntPtr hicon, out int pi);

            [PreserveSig]
            int SetOverlayImage(int iImage, int iOverlay);

            [PreserveSig]
            int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);

            [PreserveSig]
            int AddMasked(IntPtr hbmImage, int crMask, out int pi);

            [PreserveSig]
            int Draw(IntPtr pimldp);

            [PreserveSig]
            int Remove(int i);

            [PreserveSig]
            int GetIcon(int i, uint flags, out IntPtr picon);
        }

        [DllImport("user32.dll", EntryPoint = "PrivateExtractIconsW", CharSet = CharSet.Unicode)]
        private static extern uint PrivateExtractIcons(
            string szFileName,
            int nIconIndex,
            int cxIcon,
            int cyIcon,
            IntPtr[] phicon,
            uint[] piconid,
            uint nIcons,
            uint flags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(
            string lpszFile,
            int nIconIndex,
            IntPtr[] phiconLarge,
            IntPtr[] phiconSmall,
            uint nIcons);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            out SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        private const uint SHGFI_SYSICONINDEX = 0x000004000;
        private const uint ILD_TRANSPARENT = 0x00000001;

        private const int SHIL_LARGE = 0;
        private const int SHIL_SMALL = 1;
        private const int SHIL_EXTRALARGE = 2;
        private const int SHIL_JUMBO = 4;

        private static readonly int[] StandardIconResourceSizes = [16, 20, 24, 32, 40, 48, 64, 256];
    }
}
