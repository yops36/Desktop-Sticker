using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DesktopSticker.Wallpaper;

/// <summary>
/// Implementierung der Wallpaper-Verwaltung vorzugsweise über die offizielle Windows-Schnittstelle IDesktopWallpaper
/// sowie mit Win32-SystemParametersInfo-Synchronisation.
/// </summary>
public sealed class WindowsWallpaperService : IWallpaperService
{
    private const uint SPI_GETDESKWALLPAPER = 0x0073;
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDWININICHANGE = 0x02;
    private const int S_OK = 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(
        uint uiAction,
        uint uiParam,
        string pvParam,
        uint fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(
        uint uiAction,
        uint uiParam,
        StringBuilder pvParam,
        uint fWinIni);

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int nIndex);

    private const int COLOR_DESKTOP = 1;

    public WallpaperState GetCurrentState()
    {
        try
        {
            return GetStateViaCom();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException or InvalidCastException)
        {
            return GetStateViaWin32Fallback();
        }
    }

    private WallpaperState GetStateViaCom()
    {
        IDesktopWallpaper? comWallpaper = null;
        try
        {
            comWallpaper = (IDesktopWallpaper)new DesktopWallpaperComClass();

            // 1. Aktive, tatsächlich angeschlossene Monitore ermitteln:
            // Wichtig: IDesktopWallpaper.GetMonitorRECT gibt für getrennte Monitore S_FALSE (1) zurück!
            // Nur bei hr == S_OK (0) ist der Monitor tatsächlich aktiv und angeschlossen.
            var activeMonitors = new List<(string Id, NativeRect Rect)>();
            uint totalCount = comWallpaper.GetMonitorDevicePathCount();
            for (uint i = 0; i < totalCount; i++)
            {
                try
                {
                    string monitorId = comWallpaper.GetMonitorDevicePathAt(i);
                    if (string.IsNullOrWhiteSpace(monitorId))
                    {
                        continue;
                    }

                    int hr = comWallpaper.GetMonitorRECT(monitorId, out NativeRect rect);
                    if (hr == S_OK && rect.Width > 0 && rect.Height > 0)
                    {
                        activeMonitors.Add((monitorId, rect));
                    }
                }
                catch (COMException)
                {
                    // Inaktiver oder getrennter Monitor-Eintrag in Windows
                }
            }

            int screenCount = activeMonitors.Count > 0 ? activeMonitors.Count : Math.Max(1, Screen.AllScreens.Length);

            // Primärmonitor hat in Windows-Desktopkoordinaten immer den Ursprung (0, 0)
            var primaryMonitor = activeMonitors.FirstOrDefault(m => m.Rect.Left == 0 && m.Rect.Top == 0);
            if (string.IsNullOrEmpty(primaryMonitor.Id) && activeMonitors.Count > 0)
            {
                primaryMonitor = activeMonitors[0];
            }

            string primaryMonitorId = primaryMonitor.Id ?? string.Empty;

            // 2. Physische Bildschirmauflösung in echten Pixeln bestimmen
            var (physWidth, physHeight) = NativeDisplayMethods.GetPrimaryPhysicalResolution();
            if (physWidth <= 0 || physHeight <= 0)
            {
                if (!string.IsNullOrEmpty(primaryMonitor.Id))
                {
                    physWidth = primaryMonitor.Rect.Width;
                    physHeight = primaryMonitor.Rect.Height;
                }
                else if (Screen.PrimaryScreen is not null)
                {
                    physWidth = Screen.PrimaryScreen.Bounds.Width;
                    physHeight = Screen.PrimaryScreen.Bounds.Height;
                }
            }

            // 3. Hintergrundfarbe auslesen (COLORREF: 0x00BBGGRR)
            uint colorRef = comWallpaper.GetBackgroundColor();
            Color bgColor = ColorFromColorRef(colorRef);

            // 4. Positionierungsart auslesen
            DesktopWallpaperPosition rawPos = comWallpaper.GetPosition();
            WallpaperPositionMode posMode = Enum.IsDefined(typeof(WallpaperPositionMode), (int)rawPos)
                ? (WallpaperPositionMode)rawPos
                : WallpaperPositionMode.Fill;

            // 5. Status (Diashow / Spotlight / Volltonfarbe) prüfen
            DesktopSlideshowState status = DesktopSlideshowState.None;
            bool hasValidStatus = comWallpaper.GetStatus(out DesktopSlideshowState rawStatus) == S_OK;
            if (hasValidStatus)
            {
                status = rawStatus;
            }

            int? regBackgroundType = ReadExplorerBackgroundType();
            bool isSlideshow = (status & DesktopSlideshowState.Slideshow) != 0 || regBackgroundType == 2;
            bool isWallpaperDisabledInCom = hasValidStatus && (status & DesktopSlideshowState.Enabled) == 0;

            // 6. Wallpaper-Dateipfad des aktiven Monitors auslesen
            string wallpaperPath = string.Empty;
            try
            {
                if (!string.IsNullOrEmpty(primaryMonitorId))
                {
                    wallpaperPath = comWallpaper.GetWallpaper(primaryMonitorId) ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(wallpaperPath))
                {
                    wallpaperPath = comWallpaper.GetWallpaper(null) ?? string.Empty;
                }
            }
            catch (COMException)
            {
                wallpaperPath = string.Empty;
            }

            wallpaperPath = ResolveActualWallpaperSourcePath(wallpaperPath.Trim());

            bool isSpotlight = regBackgroundType == 3 || IsSpotlightWallpaperPath(wallpaperPath);
            bool isPolicyRestricted = CheckPolicyRestrictions();

            bool isSolidColorOnly = !isSlideshow && !isSpotlight &&
                                    (regBackgroundType == 1 || isWallpaperDisabledInCom || string.IsNullOrEmpty(wallpaperPath));

            bool fileExistsAndReadable = isSolidColorOnly || CanReadImageFile(wallpaperPath);

            var (isSupported, unsupportedReason) = EvaluateSupport(
                screenCount,
                physWidth,
                physHeight,
                isPolicyRestricted,
                isSlideshow,
                isSpotlight,
                posMode,
                isSolidColorOnly,
                wallpaperPath,
                fileExistsAndReadable);

            return new WallpaperState
            {
                MonitorId = primaryMonitorId,
                ConnectedMonitorCount = screenCount,
                WallpaperFilePath = isSolidColorOnly ? string.Empty : wallpaperPath,
                IsSolidColorOnly = isSolidColorOnly,
                FileExistsAndReadable = fileExistsAndReadable,
                ScreenWidth = physWidth,
                ScreenHeight = physHeight,
                PositionMode = posMode,
                BackgroundColor = bgColor,
                IsSlideshowActive = isSlideshow,
                IsSpotlightActive = isSpotlight,
                IsPolicyRestricted = isPolicyRestricted,
                IsSupportedConfiguration = isSupported,
                UnsupportedReason = unsupportedReason
            };
        }
        finally
        {
            if (comWallpaper is not null && Marshal.IsComObject(comWallpaper))
            {
                Marshal.ReleaseComObject(comWallpaper);
            }
        }
    }

    private WallpaperState GetStateViaWin32Fallback()
    {
        int screenCount = Math.Max(1, Screen.AllScreens.Length);
        var (physWidth, physHeight) = NativeDisplayMethods.GetPrimaryPhysicalResolution();
        if ((physWidth <= 0 || physHeight <= 0) && Screen.PrimaryScreen is not null)
        {
            physWidth = Screen.PrimaryScreen.Bounds.Width;
            physHeight = Screen.PrimaryScreen.Bounds.Height;
        }

        var sb = new StringBuilder(1024);
        string wallpaperPath = string.Empty;
        if (SystemParametersInfoW(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
        {
            wallpaperPath = sb.ToString().Trim();
        }

        if (string.IsNullOrEmpty(wallpaperPath))
        {
            try
            {
                using RegistryKey? deskKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
                wallpaperPath = (deskKey?.GetValue("Wallpaper") as string ?? string.Empty).Trim();
            }
            catch
            {
                // Ignorieren
            }
        }

        wallpaperPath = ResolveActualWallpaperSourcePath(wallpaperPath);

        WallpaperPositionMode posMode = ReadPositionModeFromRegistry();
        Color bgColor = ReadBackgroundColorFromRegistryOrSysColor();
        int? regBackgroundType = ReadExplorerBackgroundType();

        bool isSlideshow = regBackgroundType == 2;
        bool isSpotlight = regBackgroundType == 3 || IsSpotlightWallpaperPath(wallpaperPath);
        bool isPolicyRestricted = CheckPolicyRestrictions();
        bool isSolidColorOnly = !isSlideshow && !isSpotlight &&
                                (regBackgroundType == 1 || string.IsNullOrEmpty(wallpaperPath));
        bool fileExistsAndReadable = isSolidColorOnly || CanReadImageFile(wallpaperPath);

        var (isSupported, unsupportedReason) = EvaluateSupport(
            screenCount,
            physWidth,
            physHeight,
            isPolicyRestricted,
            isSlideshow,
            isSpotlight,
            posMode,
            isSolidColorOnly,
            wallpaperPath,
            fileExistsAndReadable);

        return new WallpaperState
        {
            MonitorId = "PRIMARY",
            ConnectedMonitorCount = screenCount,
            WallpaperFilePath = isSolidColorOnly ? string.Empty : wallpaperPath,
            IsSolidColorOnly = isSolidColorOnly,
            FileExistsAndReadable = fileExistsAndReadable,
            ScreenWidth = physWidth,
            ScreenHeight = physHeight,
            PositionMode = posMode,
            BackgroundColor = bgColor,
            IsSlideshowActive = isSlideshow,
            IsSpotlightActive = isSpotlight,
            IsPolicyRestricted = isPolicyRestricted,
            IsSupportedConfiguration = isSupported,
            UnsupportedReason = unsupportedReason
        };
    }

    public void ApplyWallpaper(string? monitorId, string imageFilePath, WallpaperPositionMode positionMode)
    {
        if (string.IsNullOrWhiteSpace(imageFilePath))
        {
            throw new ArgumentException("Der Pfad zum Hintergrundbild darf nicht leer sein.", nameof(imageFilePath));
        }

        string fullPath = Path.GetFullPath(imageFilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Die zu aktivierende Hintergrundbilddatei wurde nicht gefunden.", fullPath);
        }

        WritePositionModeToRegistry(positionMode);

        try
        {
            IDesktopWallpaper? comWallpaper = null;
            try
            {
                comWallpaper = (IDesktopWallpaper)new DesktopWallpaperComClass();
                try
                {
                    comWallpaper.Enable(true);
                }
                catch (COMException)
                {
                    // Ignorieren, falls bereits aktiv
                }

                comWallpaper.SetPosition((DesktopWallpaperPosition)positionMode);

                // Wichtig: SetWallpaper(null, fullPath) setzt das Bild auf sämtlichen Monitoreinträgen
                // von Windows, sodass auch bei zuvor getrennten/gewechselten Monitor-IDs der aktive
                // Bildschirm garantiert das neue Hintergrundbild erhält.
                comWallpaper.SetWallpaper(null, fullPath);

                if (!string.IsNullOrWhiteSpace(monitorId) && monitorId != "PRIMARY")
                {
                    try
                    {
                        comWallpaper.SetWallpaper(monitorId, fullPath);
                    }
                    catch (COMException)
                    {
                        // Bereits durch SetWallpaper(null, fullPath) abgedeckt
                    }
                }
            }
            finally
            {
                if (comWallpaper is not null && Marshal.IsComObject(comWallpaper))
                {
                    Marshal.ReleaseComObject(comWallpaper);
                }
            }

            // Zusätzlich über SystemParametersInfoW die Windows-Shell zur sofortigen Aktualisierung veranlassen
            SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, fullPath, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException or InvalidCastException)
        {
            if (!SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, fullPath, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE))
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Das Hintergrundbild konnte nicht über die Windows-Schnittstelle aktiviert werden (Win32-Fehlercode: {err}).", ex);
            }
        }
    }

    public void ApplySolidColor(Color backgroundColor)
    {
        WriteBackgroundColorToRegistry(backgroundColor);

        try
        {
            IDesktopWallpaper? comWallpaper = null;
            try
            {
                comWallpaper = (IDesktopWallpaper)new DesktopWallpaperComClass();
                comWallpaper.SetBackgroundColor(ColorToColorRef(backgroundColor));
                try
                {
                    comWallpaper.Enable(false);
                }
                catch (COMException)
                {
                    SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, string.Empty, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
                }
                return;
            }
            finally
            {
                if (comWallpaper is not null && Marshal.IsComObject(comWallpaper))
                {
                    Marshal.ReleaseComObject(comWallpaper);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException or InvalidCastException)
        {
            SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, string.Empty, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
        }
    }

    public void SetBackgroundColor(Color backgroundColor)
    {
        WriteBackgroundColorToRegistry(backgroundColor);

        try
        {
            IDesktopWallpaper? comWallpaper = null;
            try
            {
                comWallpaper = (IDesktopWallpaper)new DesktopWallpaperComClass();
                comWallpaper.SetBackgroundColor(ColorToColorRef(backgroundColor));
                return;
            }
            finally
            {
                if (comWallpaper is not null && Marshal.IsComObject(comWallpaper))
                {
                    Marshal.ReleaseComObject(comWallpaper);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException or InvalidCastException)
        {
            // Bereits in Registry geschrieben
        }
    }

    /// <summary>
    /// Falls Windows als Pfad lediglich „...\Themes\TranscodedWallpaper“ meldet, wird der echte
    /// Quellpfad aus dem Registry-Eintrag TranscodedImageCache ausgelesen (sofern diese Datei existiert).
    /// </summary>
    private static string ResolveActualWallpaperSourcePath(string reportedPath)
    {
        if (string.IsNullOrWhiteSpace(reportedPath))
        {
            return string.Empty;
        }

        if (reportedPath.EndsWith(@"\Themes\TranscodedWallpaper", StringComparison.OrdinalIgnoreCase))
        {
            string? cachedOriginalPath = TryReadPathFromTranscodedImageCache();
            if (!string.IsNullOrWhiteSpace(cachedOriginalPath) && File.Exists(cachedOriginalPath))
            {
                if (!File.Exists(reportedPath) ||
                    File.GetLastWriteTimeUtc(cachedOriginalPath) >= File.GetLastWriteTimeUtc(reportedPath).AddSeconds(-5))
                {
                    return cachedOriginalPath;
                }
            }
        }

        return reportedPath;
    }

    private static string? TryReadPathFromTranscodedImageCache()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
            if (key?.GetValue("TranscodedImageCache") is byte[] bytes && bytes.Length > 26)
            {
                int maxLen = Math.Min(bytes.Length - 24, 520) & ~1;
                string decoded = Encoding.Unicode.GetString(bytes, 24, maxLen);
                int nullIdx = decoded.IndexOf('\0');
                if (nullIdx >= 0)
                {
                    decoded = decoded[..nullIdx];
                }

                decoded = decoded.Trim();
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    return decoded;
                }
            }
        }
        catch
        {
            // Ignorieren
        }

        return null;
    }

    private static WallpaperPositionMode ReadPositionModeFromRegistry()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
            string style = (key?.GetValue("WallpaperStyle") as string ?? "10").Trim();
            string tile = (key?.GetValue("TileWallpaper") as string ?? "0").Trim();

            if (tile == "1")
            {
                return WallpaperPositionMode.Tile;
            }

            return style switch
            {
                "0" => WallpaperPositionMode.Center,
                "2" => WallpaperPositionMode.Stretch,
                "6" => WallpaperPositionMode.Fit,
                "10" => WallpaperPositionMode.Fill,
                "22" => WallpaperPositionMode.Span,
                _ => WallpaperPositionMode.Fill
            };
        }
        catch
        {
            return WallpaperPositionMode.Fill;
        }
    }

    private static void WritePositionModeToRegistry(WallpaperPositionMode mode)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
            if (key is null)
            {
                return;
            }

            switch (mode)
            {
                case WallpaperPositionMode.Tile:
                    key.SetValue("WallpaperStyle", "0", RegistryValueKind.String);
                    key.SetValue("TileWallpaper", "1", RegistryValueKind.String);
                    break;
                case WallpaperPositionMode.Center:
                    key.SetValue("WallpaperStyle", "0", RegistryValueKind.String);
                    key.SetValue("TileWallpaper", "0", RegistryValueKind.String);
                    break;
                case WallpaperPositionMode.Stretch:
                    key.SetValue("WallpaperStyle", "2", RegistryValueKind.String);
                    key.SetValue("TileWallpaper", "0", RegistryValueKind.String);
                    break;
                case WallpaperPositionMode.Fit:
                    key.SetValue("WallpaperStyle", "6", RegistryValueKind.String);
                    key.SetValue("TileWallpaper", "0", RegistryValueKind.String);
                    break;
                case WallpaperPositionMode.Span:
                    key.SetValue("WallpaperStyle", "22", RegistryValueKind.String);
                    key.SetValue("TileWallpaper", "0", RegistryValueKind.String);
                    break;
                case WallpaperPositionMode.Fill:
                default:
                    key.SetValue("WallpaperStyle", "10", RegistryValueKind.String);
                    key.SetValue("TileWallpaper", "0", RegistryValueKind.String);
                    break;
            }
        }
        catch
        {
            // Falls Registry-Schreibzugriff fehlschlägt, übernimmt IDesktopWallpaper / SystemParametersInfoW
        }
    }

    private static Color ReadBackgroundColorFromRegistryOrSysColor()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors", writable: false);
            if (key?.GetValue("Background") is string raw && !string.IsNullOrWhiteSpace(raw))
            {
                string[] parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0], out int r) &&
                    int.TryParse(parts[1], out int g) &&
                    int.TryParse(parts[2], out int b))
                {
                    return Color.FromArgb(255, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
                }
            }
        }
        catch
        {
            // Fallback auf GetSysColor
        }

        uint sysColorRef = GetSysColor(COLOR_DESKTOP);
        return ColorFromColorRef(sysColorRef);
    }

    private static void WriteBackgroundColorToRegistry(Color color)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors", writable: true);
            key?.SetValue("Background", $"{color.R} {color.G} {color.B}", RegistryValueKind.String);
        }
        catch
        {
            // Ignorieren
        }
    }

    public static (bool IsSupported, string? Reason) EvaluateSupport(
        int screenCount,
        int screenWidth,
        int screenHeight,
        bool isPolicyRestricted,
        bool isSlideshow,
        bool isSpotlight,
        WallpaperPositionMode posMode,
        bool isSolidColorOnly,
        string wallpaperPath,
        bool fileExistsAndReadable)
    {
        if (isPolicyRestricted)
        {
            return (false, "Das Ändern des Desktop-Hintergrundbilds ist auf diesem System durch Windows-Richtlinien gesperrt.");
        }

        if (screenCount > 1)
        {
            return (false, "Es wurden mehrere angeschlossene Monitore erkannt. Version 1.0 von Desktop Sticker unterstützt ausschließlich einen einzelnen Monitor.");
        }

        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return (false, "Die aktuelle Bildschirmauflösung konnte nicht eindeutig ermittelt werden.");
        }

        if (isSpotlight)
        {
            return (false, "Windows-Blickpunkt (Windows Spotlight) ist aktiv. Bitte stellen Sie in den Windows-Einstellungen zunächst ein statisches Hintergrundbild oder eine Volltonfarbe ein.");
        }

        if (isSlideshow)
        {
            return (false, "Eine automatische Hintergrundbild-Diashow ist aktiv. Desktop Sticker unterstützt nur statische Hintergrundbilder oder Volltonfarben.");
        }

        if (!isSolidColorOnly && (string.IsNullOrWhiteSpace(wallpaperPath) || !fileExistsAndReadable))
        {
            return (false, $"Die aktuelle Hintergrundbilddatei („{wallpaperPath}“) existiert nicht oder kann nicht gelesen werden.");
        }

        return (true, null);
    }

    public static bool CanReadImageFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 16)
            {
                return false;
            }

            using var img = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            return img.Width > 0 && img.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private static int? ReadExplorerBackgroundType()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers", writable: false);
            if (key?.GetValue("BackgroundType") is int bgType)
            {
                return bgType;
            }
        }
        catch
        {
            // Ignorieren
        }

        return null;
    }

    private static bool IsSpotlightWallpaperPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.Contains("Microsoft.Windows.ContentDeliveryManager", StringComparison.OrdinalIgnoreCase) ||
               (path.Contains(@"Windows\SystemApps\MicrosoftWindows.Client.CBS", StringComparison.OrdinalIgnoreCase) &&
                path.Contains("Spotlight", StringComparison.OrdinalIgnoreCase)) ||
               path.Contains("IrisService", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CheckPolicyRestrictions()
    {
        try
        {
            using RegistryKey? hkcuActiveDesktop = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop", writable: false);
            if (hkcuActiveDesktop?.GetValue("NoChangingWallPaper") is int val1 && val1 != 0)
            {
                return true;
            }

            using RegistryKey? hklmActiveDesktop = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop", writable: false);
            if (hklmActiveDesktop?.GetValue("NoChangingWallPaper") is int val2 && val2 != 0)
            {
                return true;
            }

            using RegistryKey? hkcuSystem = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", writable: false);
            if (hkcuSystem?.GetValue("Wallpaper") is string forcedWallpaper && !string.IsNullOrWhiteSpace(forcedWallpaper))
            {
                return true;
            }
        }
        catch
        {
            // Kein Zugriff auf Policy-Schlüssel bedeutet keine Einschränkung
        }

        return false;
    }

    public static Color ColorFromColorRef(uint colorRef)
    {
        int r = (int)(colorRef & 0xFF);
        int g = (int)((colorRef >> 8) & 0xFF);
        int b = (int)((colorRef >> 16) & 0xFF);
        return Color.FromArgb(255, r, g, b);
    }

    public static uint ColorToColorRef(Color color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }
}
