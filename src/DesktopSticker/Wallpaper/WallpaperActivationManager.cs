using System.Drawing;
using DesktopSticker.Backup;
using DesktopSticker.Configuration;
using DesktopSticker.Imaging;

namespace DesktopSticker.Wallpaper;

public enum ActivationStatus
{
    Success,
    WallpaperChangedDuringEditing,
    GeneratedImageInvalid,
    ActivationFailed
}

public sealed record ActivationResult(
    ActivationStatus Status,
    string? ErrorMessage = null);

/// <summary>
/// Steuert das sichere Erzeugen, Verifizieren und Aktivieren des zusammengesetzten Wallpapers (Kapitel 10, 11, 14).
/// </summary>
public sealed class WallpaperActivationManager
{
    private const string GeneratedFileA = "DesktopSticker_Generated_A.bmp";
    private const string GeneratedFileB = "DesktopSticker_Generated_B.bmp";

    private readonly AppStoragePaths _paths;
    private readonly IWallpaperService _wallpaperService;
    private readonly BackupManager _backupManager;
    private readonly SettingsManager _settingsManager;

    public WallpaperActivationManager(
        AppStoragePaths paths,
        IWallpaperService wallpaperService,
        BackupManager backupManager,
        SettingsManager settingsManager)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _backupManager = backupManager ?? throw new ArgumentNullException(nameof(backupManager));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
    }

    /// <summary>
    /// Prüft, ob sich die Windows-Wallpaper-Konfiguration seit dem Öffnen des Bearbeitungsfensters verändert hat (Kapitel 11.3 & 21).
    /// </summary>
    public static bool HasConfigurationChangedSinceSessionStart(
        WallpaperState sessionStartState,
        WallpaperState currentOsState)
    {
        ArgumentNullException.ThrowIfNull(sessionStartState);
        ArgumentNullException.ThrowIfNull(currentOsState);

        if (!currentOsState.IsSupportedConfiguration)
        {
            return true;
        }

        if (sessionStartState.IsSolidColorOnly != currentOsState.IsSolidColorOnly)
        {
            return true;
        }

        if (sessionStartState.IsSolidColorOnly)
        {
            return sessionStartState.BackgroundColor.R != currentOsState.BackgroundColor.R ||
                   sessionStartState.BackgroundColor.G != currentOsState.BackgroundColor.G ||
                   sessionStartState.BackgroundColor.B != currentOsState.BackgroundColor.B;
        }

        if (!BackupManager.PathsEqual(sessionStartState.WallpaperFilePath, currentOsState.WallpaperFilePath))
        {
            return true;
        }

        if (sessionStartState.PositionMode != currentOsState.PositionMode)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Führt die vollständige Komposition und sichere Aktivierung als Windows-Hintergrundbild durch (Kapitel 10 & 11).
    /// </summary>
    public ActivationResult ComposeAndActivate(
        WallpaperState sessionStartOsState,
        AppSettings workingSettings,
        Bitmap? originalWallpaperBitmap,
        Bitmap stickerBitmap)
    {
        ArgumentNullException.ThrowIfNull(sessionStartOsState);
        ArgumentNullException.ThrowIfNull(workingSettings);
        ArgumentNullException.ThrowIfNull(stickerBitmap);

        // 1. Prüfen, ob sich die aktuelle Wallpaper-Konfiguration seit dem Öffnen der Bearbeitung verändert hat (Schritt 3 & 4)
        WallpaperState liveOsState = _wallpaperService.GetCurrentState();
        if (HasConfigurationChangedSinceSessionStart(sessionStartOsState, liveOsState))
        {
            return new ActivationResult(
                ActivationStatus.WallpaperChangedDuringEditing,
                "Das Windows-Hintergrundbild oder dessen Anzeigeeinstellung wurde während der Bearbeitung außerhalb von Desktop Sticker verändert. Bitte laden Sie die Bearbeitung neu.");
        }

        try
        {
            _paths.EnsureDirectoriesAndWriteAccess();

            // 2. Falls noch keine Originalsicherung existiert, jetzt vor der ersten Veränderung sichern (Kapitel 5)
            if (!workingSettings.HasOriginalBackup)
            {
                _backupManager.CreateOrUpdateOriginalBackup(liveOsState, workingSettings);
            }

            if (!_backupManager.HasValidOriginalBackup(workingSettings))
            {
                return new ActivationResult(
                    ActivationStatus.ActivationFailed,
                    "Das ursprüngliche Hintergrundbild konnte nicht zuverlässig gesichert oder geladen werden. Der Desktop wurde nicht verändert.");
            }

            int screenWidth = liveOsState.ScreenWidth;
            int screenHeight = liveOsState.ScreenHeight;

            // 3. Neue Ziel-BMP-Datei bestimmen (Wechsel zwischen A und B, damit die bisherige gültige Datei bei Fehlern erhalten bleibt)
            string currentGenName = Path.GetFileName(workingSettings.CurrentGeneratedWallpaperPath ?? string.Empty);
            string nextGenName = string.Equals(currentGenName, GeneratedFileA, StringComparison.OrdinalIgnoreCase)
                ? GeneratedFileB
                : GeneratedFileA;

            string nextGeneratedPath = Path.Combine(_paths.GeneratedDir, nextGenName);
            string? previousGeneratedPath = workingSettings.CurrentGeneratedWallpaperPath;

            // 4. Hintergrundbild und Sticker zusammensetzen (Kapitel 10)
            using (Bitmap composed = WallpaperComposer.ComposeWallpaper(
                originalWallpaper: originalWallpaperBitmap,
                isSolidColorOnly: workingSettings.OriginalWasSolidColorOnly,
                backgroundColor: workingSettings.OriginalBackgroundColor,
                originalPositionMode: workingSettings.OriginalPositionMode,
                stickerBitmap: stickerBitmap,
                screenWidth: screenWidth,
                screenHeight: screenHeight,
                relativeWidth: workingSettings.RelativeWidth,
                relativeX: workingSettings.RelativeX,
                relativeY: workingSettings.RelativeY,
                cornerRadiusPercent: workingSettings.CornerRadiusPercent))
            {
                WallpaperComposer.SaveComposedWallpaperAsBmp(composed, nextGeneratedPath, _paths.TempDir);
            }

            // 5. Prüfen, ob das generierte Bild existiert und vollständig lesbar ist (Kapitel 11.1 & 11.2)
            if (!File.Exists(nextGeneratedPath) || !WindowsWallpaperService.CanReadImageFile(nextGeneratedPath))
            {
                return new ActivationResult(
                    ActivationStatus.GeneratedImageInvalid,
                    "Die erzeugte Hintergrundbilddatei ist ungültig oder nicht lesbar. Die bisherige Konfiguration bleibt unverändert.");
            }

            string newGeneratedHash = AppStoragePaths.ComputeFileSha256(nextGeneratedPath);

            // 6. Über die Windows-Schnittstelle aktivieren (Anzeigemodus auf Bildschirmfüllung abstimmen, Kapitel 10 & 11.5)
            _wallpaperService.ApplyWallpaper(
                liveOsState.MonitorId,
                nextGeneratedPath,
                WallpaperPositionMode.Fill);

            // 7. Überprüfen, ob Windows die Änderung erfolgreich angenommen hat (Kapitel 11.6)
            WallpaperState verifyState = _wallpaperService.GetCurrentState();
            if (verifyState.IsSolidColorOnly ||
                (!BackupManager.PathsEqual(verifyState.WallpaperFilePath, nextGeneratedPath) &&
                 !HasSameHash(verifyState.WallpaperFilePath, newGeneratedHash)))
            {
                return new ActivationResult(
                    ActivationStatus.ActivationFailed,
                    "Windows hat das neue Hintergrundbild nicht übernommen. Die bisherige Konfiguration bleibt erhalten.");
            }

            // 8. Erfolgreichen Zustand in den Einstellungen speichern (Kapitel 11.7)
            StickerPlacementResult finalPlacement = StickerGeometry.CalculatePlacement(
                stickerBitmap.Width,
                stickerBitmap.Height,
                screenWidth,
                screenHeight,
                workingSettings.RelativeWidth,
                workingSettings.RelativeX,
                workingSettings.RelativeY);

            workingSettings.RelativeWidth = finalPlacement.RelativeWidth;
            workingSettings.RelativeX = finalPlacement.RelativeX;
            workingSettings.RelativeY = finalPlacement.RelativeY;
            workingSettings.CurrentGeneratedWallpaperPath = nextGeneratedPath;
            workingSettings.CurrentGeneratedWallpaperHash = newGeneratedHash;
            workingSettings.IsStickerActive = true;
            workingSettings.LastGeneratedScreenWidth = screenWidth;
            workingSettings.LastGeneratedScreenHeight = screenHeight;

            _settingsManager.Save(workingSettings);

            // 9. Erst nach erfolgreichem Aktivieren und Speichern alte Dateien aufräumen (Kapitel 11 & 14)
            if (!string.IsNullOrWhiteSpace(previousGeneratedPath) &&
                !BackupManager.PathsEqual(previousGeneratedPath, nextGeneratedPath) &&
                File.Exists(previousGeneratedPath))
            {
                try
                {
                    File.Delete(previousGeneratedPath);
                }
                catch
                {
                    // Ignorieren
                }
            }

            StickerImageLoader.CleanupUnusedStickerCopies(_paths, workingSettings.StickerFilePath);
            _paths.CleanupTempDirectory();

            return new ActivationResult(ActivationStatus.Success);
        }
        catch (Exception ex)
        {
            return new ActivationResult(
                ActivationStatus.ActivationFailed,
                $"Fehler beim Erzeugen oder Aktivieren des Hintergrundbildes: {ex.Message}");
        }
    }

    private static bool HasSameHash(string? filePath, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                AppStoragePaths.ComputeFileSha256(filePath),
                expectedHash,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
