using System.Drawing;

namespace DesktopSticker.Wallpaper;

/// <summary>
/// Unterstützte Anzeigemodi für statische Windows-Hintergrundbilder.
/// Entspricht den Windows-Einstellungen: Zentrieren, Kacheln, Strecken, Anpassen, Ausfüllen, Übergreifend.
/// </summary>
public enum WallpaperPositionMode
{
    Center = 0,  // DWPOS_CENTER (Zentrieren)
    Tile = 1,    // DWPOS_TILE (Kacheln)
    Stretch = 2, // DWPOS_STRETCH (Strecken)
    Fit = 3,     // DWPOS_FIT (Anpassen)
    Fill = 4,    // DWPOS_FILL (Ausfüllen)
    Span = 5     // DWPOS_SPAN (Übergreifend - in Version 1.0 nicht unterstützt)
}

/// <summary>
/// Vollständiger Zustand der aktuell erkannten Windows-Desktop-Hintergrundkonfiguration (Entwicklungsschritt 1).
/// </summary>
public sealed record WallpaperState
{
    /// <summary>Eindeutige Monitor-ID von IDesktopWallpaper.</summary>
    public string MonitorId { get; init; } = string.Empty;

    /// <summary>Anzahl der aktuell aktiven angeschlossenen Monitore.</summary>
    public int ConnectedMonitorCount { get; init; }

    /// <summary>Pfad der aktuellen Hintergrundbilddatei (leer bei rein einfarbigem Hintergrund).</summary>
    public string WallpaperFilePath { get; init; } = string.Empty;

    /// <summary>Gibt an, ob kein Bild verwendet wird, sondern ausschließlich eine Hintergrundfarbe.</summary>
    public bool IsSolidColorOnly { get; init; }

    /// <summary>Gibt an, ob die angegebene Hintergrundbilddatei tatsächlich existiert und als Bild gelesen werden kann.</summary>
    public bool FileExistsAndReadable { get; init; }

    /// <summary>Tatsächliche Bildschirmbreite in physischen Pixeln.</summary>
    public int ScreenWidth { get; init; }

    /// <summary>Tatsächliche Bildschirmhöhe in physischen Pixeln.</summary>
    public int ScreenHeight { get; init; }

    /// <summary>Aktuelle Wallpaper-Positionierungsart in Windows.</summary>
    public WallpaperPositionMode PositionMode { get; init; }

    /// <summary>Aktuelle Desktop-Hintergrundfarbe.</summary>
    public Color BackgroundColor { get; init; }

    /// <summary>Gibt an, ob eine automatische Hintergrundbild-Diashow aktiv ist.</summary>
    public bool IsSlideshowActive { get; init; }

    /// <summary>Gibt an, ob Windows Spotlight (Windows-Blickpunkt) aktiv ist.</summary>
    public bool IsSpotlightActive { get; init; }

    /// <summary>Gibt an, ob Windows-Richtlinien das Ändern des Hintergrundbildes verbieten.</summary>
    public bool IsPolicyRestricted { get; init; }

    /// <summary>Gibt an, ob eine durch Version 1.0 unterstützte Konfiguration vorliegt.</summary>
    public bool IsSupportedConfiguration { get; init; }

    /// <summary>Verständliche deutsche Begründung, falls die Konfiguration nicht unterstützt wird.</summary>
    public string? UnsupportedReason { get; init; }
}
