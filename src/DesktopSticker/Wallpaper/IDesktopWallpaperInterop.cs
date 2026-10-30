using System.Runtime.InteropServices;

namespace DesktopSticker.Wallpaper;

[StructLayout(LayoutKind.Sequential)]
public struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

public enum DesktopWallpaperPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5
}

[Flags]
public enum DesktopSlideshowOptions : uint
{
    None = 0,
    ShuffleImages = 0x01
}

[Flags]
public enum DesktopSlideshowState : uint
{
    None = 0,
    Enabled = 0x01,
    Slideshow = 0x02,
    DisabledByRemoteSession = 0x04
}

public enum DesktopSlideshowDirection
{
    Forward = 0,
    Backward = 1
}

/// <summary>
/// Offizielle Windows-COM-Schnittstelle IDesktopWallpaper (ShObjIdl_core.h).
/// </summary>
[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDesktopWallpaper
{
    void SetWallpaper(
        [MarshalAs(UnmanagedType.LPWStr)] string? monitorID,
        [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetWallpaper(
        [MarshalAs(UnmanagedType.LPWStr)] string? monitorID);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetMonitorDevicePathAt(uint monitorIndex);

    uint GetMonitorDevicePathCount();

    /// <summary>
    /// Wichtig: Gibt für angeschlossene Monitore S_OK (0) und für getrennte Monitore S_FALSE (1) zurück.
    /// Ohne [PreserveSig] würde .NET S_FALSE (1) fälschlich als Erfolg behandeln!
    /// </summary>
    [PreserveSig]
    int GetMonitorRECT(
        [MarshalAs(UnmanagedType.LPWStr)] string monitorID,
        out NativeRect displayRect);

    void SetBackgroundColor(uint color);

    uint GetBackgroundColor();

    void SetPosition(DesktopWallpaperPosition position);

    DesktopWallpaperPosition GetPosition();

    void SetSlideshow(IntPtr items);

    IntPtr GetSlideshow();

    void SetSlideshowOptions(DesktopSlideshowOptions options, uint slideshowTick);

    void GetSlideshowOptions(out DesktopSlideshowOptions options, out uint slideshowTick);

    void AdvanceSlideshow(
        [MarshalAs(UnmanagedType.LPWStr)] string? monitorID,
        DesktopSlideshowDirection direction);

    [PreserveSig]
    int GetStatus(out DesktopSlideshowState state);

    void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

[ComImport]
[Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
public class DesktopWallpaperComClass
{
}

internal static class NativeDisplayMethods
{
    public const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplaySettingsW(
        string? lpszDeviceName,
        int iModeNum,
        ref DEVMODEW lpDevMode);

    public static (int Width, int Height) GetPrimaryPhysicalResolution()
    {
        var devMode = new DEVMODEW
        {
            dmSize = (ushort)Marshal.SizeOf<DEVMODEW>()
        };

        if (EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref devMode) &&
            devMode.dmPelsWidth > 0 && devMode.dmPelsHeight > 0)
        {
            return ((int)devMode.dmPelsWidth, (int)devMode.dmPelsHeight);
        }

        return (0, 0);
    }
}
