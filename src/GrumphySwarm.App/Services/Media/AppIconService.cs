using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GrumphySwarm.App.Services.Media;

/// <summary>
/// Extracts the real icon of the app publishing the current media session.
/// Packaged (MSIX/UWP) sources resolve through the package manifest logo;
/// classic Win32 sources through their executable icon. Results are cached
/// per source id, frozen, and any failure returns null — the UI then falls
/// back to the generic note badge instead of showing a wrong icon.
/// </summary>
public sealed class AppIconService
{
    // ---- Win32 executable icon ---------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;

    private readonly object _gate = new();
    private readonly Dictionary<string, ImageSource?> _cache = [];

    public ImageSource? GetIcon(string sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return null;
        }

        lock (_gate)
        {
            if (_cache.TryGetValue(sourceAppUserModelId, out var cached))
            {
                return cached;
            }
        }

        var icon = TryGetIcon(sourceAppUserModelId);

        lock (_gate)
        {
            _cache[sourceAppUserModelId] = icon;
        }

        return icon;
    }

    private static ImageSource? TryGetIcon(string aumId)
    {
        try
        {
            var bang = aumId.IndexOf('!');
            if (bang > 0)
            {
                return TryGetPackagedLogo(aumId[..bang]);
            }

            if (aumId.Contains(":\\") || aumId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetExecutableIcon(aumId);
            }
        }
        catch
        {
            // Icon lookup is cosmetic — never let it surface.
        }

        return null;
    }

    private static ImageSource? TryGetPackagedLogo(string packageFamilyName)
    {
        var manager = new global::Windows.Management.Deployment.PackageManager();
        var package = manager.FindPackageForUser(string.Empty, packageFamilyName);
        if (package is null || package.Logo is null || string.IsNullOrEmpty(package.InstalledPath))
        {
            return null;
        }

        // Package.Logo is either an absolute file URI into WindowsApps or an
        // ms-appx path relative to the install folder.
        var logoPath = package.Logo.IsFile
            ? package.Logo.LocalPath
            : Path.Combine(package.InstalledPath, package.Logo.OriginalString.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(logoPath))
        {
            return null;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 48;
        bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource? TryGetExecutableIcon(string exePath)
    {
        if (!File.Exists(exePath))
        {
            return null;
        }

        var info = new SHFILEINFO();
        var result = SHGetFileInfo(exePath, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}
