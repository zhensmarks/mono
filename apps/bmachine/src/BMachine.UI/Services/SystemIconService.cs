using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace BMachine.UI.Services;

/// <summary>
/// Extracts the real Windows shell icon for a path (shell32 / imageres, including
/// 7tsp-replaced themes) so the Explorer matches the OS appearance. Windows only;
/// returns null on other platforms so callers can keep the built-in glyph icons.
/// </summary>
internal static class SystemIconService
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint DI_NORMAL = 0x0003;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hgo);

    /// <summary>Returns raw BGRA pixels (premultiplied alpha), size x size. Can run on a background thread.</summary>
    public static byte[]? GetIconBytes(string path, int size)
    {
        if (!OperatingSystem.IsWindows() || size <= 0) return null;

        var shfi = new SHFILEINFO();
        var hIcon = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (hIcon == IntPtr.Zero) return null;

        try
        {
            return RenderIcon(hIcon, size);
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static byte[]? RenderIcon(IntPtr hIcon, int size)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        var memDc = CreateCompatibleDC(screenDc);
        if (memDc == IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            return null;
        }

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };

        var dib = CreateDIBSection(memDc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero)
        {
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
            return null;
        }

        var oldObject = SelectObject(memDc, dib);
        DrawIconEx(memDc, 0, 0, hIcon, size, size, 0, IntPtr.Zero, DI_NORMAL);
        SelectObject(memDc, oldObject);

        var bytes = new byte[size * size * 4];
        if (bits != IntPtr.Zero)
            Marshal.Copy(bits, bytes, 0, bytes.Length);

        DeleteObject(dib);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);

        // DrawIconEx writes straight (unpremultiplied) alpha; premultiply for Bgra8888 PreMul.
        for (int i = 0; i < bytes.Length; i += 4)
        {
            byte a = bytes[i + 3];
            if (a == 0 || a == 255) continue;
            bytes[i] = (byte)(bytes[i] * a / 255);
            bytes[i + 1] = (byte)(bytes[i + 1] * a / 255);
            bytes[i + 2] = (byte)(bytes[i + 2] * a / 255);
        }

        return bytes;
    }

    /// <summary>Wraps raw BGRA bytes into an Avalonia bitmap. Must run on the UI thread.</summary>
    public static WriteableBitmap? CreateBitmap(byte[] bgra, int size)
    {
        if (bgra == null || size <= 0 || bgra.Length < size * size * 4) return null;
        var wb = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            Marshal.Copy(bgra, 0, fb.Address, size * size * 4);
        }
        return wb;
    }

    public static byte[]? GetIconBytesFromModule(string modulePath, int index, int size)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(modulePath) || !System.IO.File.Exists(modulePath)) return null;
        IntPtr hIconLarge = IntPtr.Zero;
        IntPtr hIconSmall = IntPtr.Zero;
        try
        {
            var count = ExtractIconEx(modulePath, index, out hIconLarge, out hIconSmall, 1);
            if (count > 0 && hIconLarge != IntPtr.Zero)
            {
                return RenderIcon(hIconLarge, size);
            }
            if (count > 0 && hIconSmall != IntPtr.Zero)
            {
                return RenderIcon(hIconSmall, size);
            }
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hIconLarge != IntPtr.Zero) DestroyIcon(hIconLarge);
            if (hIconSmall != IntPtr.Zero) DestroyIcon(hIconSmall);
        }
    }

    /// <summary>
    /// Resolves an icon for the given target path, first checking any custom 7tsp icon pack path,
    /// and falling back to standard Windows shell icons.
    /// </summary>
    public static Bitmap? ResolveIcon(string targetPath, bool isDirectory, string? customIconPath, int size)
    {
        // 1. Try custom 7tsp / icon pack path if configured
        if (!string.IsNullOrWhiteSpace(customIconPath))
        {
            try
            {
                var bmp = ResolveCustomIcon(targetPath, isDirectory, customIconPath.Trim(), size);
                if (bmp != null) return bmp;
            }
            catch
            {
                // Fallback to system icon
            }
        }

        // 2. Fallback to Windows shell icon
        try
        {
            var bytes = GetIconBytes(targetPath, size);
            if (bytes != null)
                return CreateBitmap(bytes, size);
        }
        catch { }

        return null;
    }

    private static Bitmap? ResolveCustomIcon(string targetPath, bool isDirectory, string customPath, int size)
    {
        // A) If customPath is a directory (e.g. extracted 7tsp icon pack)
        if (System.IO.Directory.Exists(customPath))
        {
            string? foundFile = null;

            if (isDirectory)
            {
                string[] candidates = { "folder.ico", "folder.png", "dir.ico", "directory.ico", "3.ico", "4.ico", "Folder.ico", "Folder.png" };
                foreach (var cand in candidates)
                {
                    var p = System.IO.Path.Combine(customPath, cand);
                    if (System.IO.File.Exists(p)) { foundFile = p; break; }
                    var sub = System.IO.Path.Combine(customPath, "Resources", cand);
                    if (System.IO.File.Exists(sub)) { foundFile = sub; break; }
                    var sub2 = System.IO.Path.Combine(customPath, "Icons", cand);
                    if (System.IO.File.Exists(sub2)) { foundFile = sub2; break; }
                }
            }
            else
            {
                var ext = System.IO.Path.GetExtension(targetPath).TrimStart('.').ToLowerInvariant();
                string[] candidates = { $"{ext}.ico", $"{ext}.png", $"file_{ext}.ico", "file.ico", "document.ico", "default.ico", "0.ico", "2.ico" };
                foreach (var cand in candidates)
                {
                    var p = System.IO.Path.Combine(customPath, cand);
                    if (System.IO.File.Exists(p)) { foundFile = p; break; }
                    var sub = System.IO.Path.Combine(customPath, "Resources", cand);
                    if (System.IO.File.Exists(sub)) { foundFile = sub; break; }
                    var sub2 = System.IO.Path.Combine(customPath, "Icons", cand);
                    if (System.IO.File.Exists(sub2)) { foundFile = sub2; break; }
                }
            }

            if (!string.IsNullOrEmpty(foundFile) && System.IO.File.Exists(foundFile))
            {
                return new Bitmap(foundFile);
            }

            // Also check for embedded library in folder (e.g. imageres.dll, imageres.dll.res, shell32.dll)
            string[] moduleCandidates = { "imageres.dll", "imageres.dll.res", "shell32.dll", "shell32.dll.res", "icons.dll" };
            foreach (var mod in moduleCandidates)
            {
                var p = System.IO.Path.Combine(customPath, mod);
                if (!System.IO.File.Exists(p))
                    p = System.IO.Path.Combine(customPath, "Resources", mod);
                if (System.IO.File.Exists(p))
                {
                    int index = isDirectory ? 3 : 0;
                    var bytes = GetIconBytesFromModule(p, index, size);
                    if (bytes != null)
                        return CreateBitmap(bytes, size);
                }
            }
        }
        // B) If customPath is a file
        else if (System.IO.File.Exists(customPath))
        {
            var ext = System.IO.Path.GetExtension(customPath).ToLowerInvariant();
            if (ext is ".ico" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
            {
                return new Bitmap(customPath);
            }
            else if (ext is ".dll" or ".exe" or ".res" or ".icl")
            {
                int index = isDirectory ? 3 : 0;
                var bytes = GetIconBytesFromModule(customPath, index, size);
                if (bytes != null)
                    return CreateBitmap(bytes, size);
            }
        }

        return null;
    }
}