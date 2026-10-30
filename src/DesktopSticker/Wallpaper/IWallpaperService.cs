using System.Drawing;

namespace DesktopSticker.Wallpaper;

/// <summary>
/// Schnittstelle für das Auslesen und Setzen des Windows-Desktop-Hintergrunds.
/// </summary>
public interface IWallpaperService
{
    /// <summary>
    /// Liest den aktuellen Zustand des Desktop-Hintergrunds aus, ohne Änderungen am Desktop vorzunehmen (Phase 2).
    /// </summary>
    WallpaperState GetCurrentState();

    /// <summary>
    /// Aktiviert eine Bilddatei als neues Windows-Hintergrundbild mit dem angegebenen Anzeigemodus (Phase 9).
    /// </summary>
    void ApplyWallpaper(string? monitorId, string imageFilePath, WallpaperPositionMode positionMode);

    /// <summary>
    /// Stellt einen rein einfarbigen Desktop-Hintergrund wieder her (Phase 13).
    /// </summary>
    void ApplySolidColor(Color backgroundColor);

    /// <summary>
    /// Setzt die Hintergrundfarbe des Desktops.
    /// </summary>
    void SetBackgroundColor(Color backgroundColor);
}
