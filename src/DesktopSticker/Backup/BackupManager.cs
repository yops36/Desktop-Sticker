using DesktopSticker.Configuration;
using DesktopSticker.Localization;
using DesktopSticker.Wallpaper;

namespace DesktopSticker.Backup;

public enum RestoreResultStatus
{
    Success,
    ExternalWallpaperChanged,
    MissingOrInvalidBackup,
    VerificationFailed
}

public sealed record RestoreResult(
    RestoreResultStatus Status,
    string? Message = null);

/// <summary>
/// Verwaltet die Sicherung und Wiederherstellung des ursprünglichen Desktop-Hintergrundbildes (Kapitel 5, 12, 15, 18.1).
/// </summary>
public sealed class BackupManager
{
    private readonly AppStoragePaths _paths;

    public BackupManager(AppStoragePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>
    /// Entscheidende Schutzregel (Kapitel 5):
    /// Prüft, ob das angegebene Hintergrundbild ein von Desktop Sticker generiertes Bild ist.
    /// </summary>
    public bool IsGeneratedWallpaper(WallpaperState osState, AppSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(osState);

        if (osState.IsSolidColorOnly || string.IsNullOrWhiteSpace(osState.WallpaperFilePath))
        {
            return false;
        }

        if (_paths.IsPathInsideGeneratedDirectory(osState.WallpaperFilePath))
        {
            return true;
        }

        if (settings is not null)
        {
            if (!string.IsNullOrWhiteSpace(settings.CurrentGeneratedWallpaperPath) &&
                PathsEqual(osState.WallpaperFilePath, settings.CurrentGeneratedWallpaperPath))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(settings.CurrentGeneratedWallpaperHash) &&
                File.Exists(osState.WallpaperFilePath))
            {
                try
                {
                    string activeHash = AppStoragePaths.ComputeFileSha256(osState.WallpaperFilePath);
                    if (string.Equals(activeHash, settings.CurrentGeneratedWallpaperHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignorieren
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Prüft, ob der Benutzer außerhalb von Desktop Sticker ein anderes Hintergrundbild eingestellt hat (Kapitel 15 & 18.1).
    /// </summary>
    public bool HasExternalWallpaperChanged(WallpaperState osState, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(osState);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.HasOriginalBackup)
        {
            return false;
        }

        // Ein von Desktop Sticker erzeugtes Hintergrundbild gilt niemals als externe Benutzeränderung
        if (IsGeneratedWallpaper(osState, settings))
        {
            return false;
        }

        if (settings.IsStickerActive && !string.IsNullOrWhiteSpace(settings.CurrentGeneratedWallpaperPath))
        {
            // Wenn ein zusammengesetztes Sticker-Wallpaper aktiv war, muss das aktuelle Windows-Wallpaper unser generiertes Bild sein
            return true;
        }

        // Wenn der Sticker deaktiviert war, prüfen ob sich der Hintergrund gegenüber dem Original verändert hat
        if (settings.OriginalWasSolidColorOnly != osState.IsSolidColorOnly)
        {
            return true;
        }

        if (settings.OriginalWasSolidColorOnly)
        {
            return settings.OriginalBackgroundColorR != osState.BackgroundColor.R ||
                   settings.OriginalBackgroundColorG != osState.BackgroundColor.G ||
                   settings.OriginalBackgroundColorB != osState.BackgroundColor.B;
        }

        if (PathsEqual(osState.WallpaperFilePath, settings.OriginalBackupFilePath) ||
            PathsEqual(osState.WallpaperFilePath, settings.OriginalSourceFilePath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(settings.OriginalBackupFileHash) && File.Exists(osState.WallpaperFilePath))
        {
            try
            {
                string currentHash = AppStoragePaths.ComputeFileSha256(osState.WallpaperFilePath);
                if (string.Equals(currentHash, settings.OriginalBackupFileHash, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch
            {
                // Ignorieren
            }
        }

        return true;
    }

    /// <summary>
    /// Prüft, ob eine gültige und lesbare Originalsicherung vorliegt.
    /// </summary>
    public bool HasValidOriginalBackup(AppSettings settings)
    {
        if (settings is null || !settings.HasOriginalBackup)
        {
            return false;
        }

        if (settings.OriginalWasSolidColorOnly)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(settings.OriginalBackupFilePath) &&
               WindowsWallpaperService.CanReadImageFile(settings.OriginalBackupFilePath);
    }

    /// <summary>
    /// Erstellt eine neue Sicherung des ursprünglichen Desktop-Hintergrunds im lokalen Anwendungsdatenverzeichnis.
    /// Verweigert strikt die Sicherung eines bereits von Desktop Sticker generierten Bildes (Schutzregel Kapitel 5).
    /// </summary>
    public void CreateOrUpdateOriginalBackup(WallpaperState osState, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(osState);
        ArgumentNullException.ThrowIfNull(settings);

        if (!osState.IsSupportedConfiguration)
        {
            throw new InvalidOperationException(
                osState.UnsupportedReason ?? "Die aktuelle Desktop-Konfiguration wird nicht unterstützt.");
        }

        // Entscheidende Schutzregel (Kapitel 5):
        // Ein bereits von Desktop Sticker generiertes Hintergrundbild darf niemals als Original gespeichert werden!
        if (IsGeneratedWallpaper(osState, settings) ||
            (settings.IsStickerActive && HasValidOriginalBackup(settings) && IsTranscodedWallpaperPath(osState.WallpaperFilePath)))
        {
            if (HasValidOriginalBackup(settings))
            {
                // Bestehende Originalsicherung unverändert beibehalten
                return;
            }

            throw new InvalidOperationException(
                "Schutzregel verletzt: Das aktuell aktive Hintergrundbild wurde bereits von Desktop Sticker erzeugt und darf nicht als Originalhintergrund gesichert werden.");
        }

        // Wenn das aktuell aktive Wallpaper bereits auf unsere eigene Sicherungsdatei im Originals-Ordner zeigt
        // (z. B. nach der nahtlosen Wiederherstellung beim Öffnen des Editors), darf die Sicherung nicht über sich
        // selbst kopiert werden, damit OriginalSourceFilePath dauerhaft erhalten bleibt.
        if (!osState.IsSolidColorOnly &&
            HasValidOriginalBackup(settings) &&
            PathsEqual(osState.WallpaperFilePath, settings.OriginalBackupFilePath))
        {
            return;
        }

        _paths.EnsureDirectoriesAndWriteAccess();

        if (osState.IsSolidColorOnly)
        {
            settings.HasOriginalBackup = true;
            settings.MonitorId = osState.MonitorId;
            settings.OriginalWasSolidColorOnly = true;
            settings.OriginalBackupFilePath = string.Empty;
            settings.OriginalSourceFilePath = string.Empty;
            settings.OriginalBackupFileHash = string.Empty;
            settings.OriginalBackgroundColorR = osState.BackgroundColor.R;
            settings.OriginalBackgroundColorG = osState.BackgroundColor.G;
            settings.OriginalBackgroundColorB = osState.BackgroundColor.B;
            settings.OriginalPositionMode = osState.PositionMode;
            settings.OriginalScreenWidth = osState.ScreenWidth;
            settings.OriginalScreenHeight = osState.ScreenHeight;
            return;
        }

        if (string.IsNullOrWhiteSpace(osState.WallpaperFilePath) || !WindowsWallpaperService.CanReadImageFile(osState.WallpaperFilePath))
        {
            throw new IOException($"Das ursprüngliche Hintergrundbild („{osState.WallpaperFilePath}“) kann nicht gelesen und daher nicht gesichert werden.");
        }

        string ext = Path.GetExtension(osState.WallpaperFilePath);
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = ".bmp";
        }

        string tempBackupPath = Path.Combine(_paths.TempDir, $"orig_backup_{Guid.NewGuid():N}{ext}");
        string finalBackupPath = Path.Combine(_paths.OriginalsDir, $"OriginalWallpaper{ext}");

        try
        {
            string sourceHash = AppStoragePaths.ComputeFileSha256(osState.WallpaperFilePath);
            File.Copy(osState.WallpaperFilePath, tempBackupPath, overwrite: true);

            if (!WindowsWallpaperService.CanReadImageFile(tempBackupPath))
            {
                throw new IOException("Die angelegte Sicherungskopie des Hintergrundbildes konnte nicht verifiziert werden.");
            }

            string copyHash = AppStoragePaths.ComputeFileSha256(tempBackupPath);
            if (!string.Equals(sourceHash, copyHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Prüfsummenfehler beim Sichern des ursprünglichen Hintergrundbildes.");
            }

            string? previousBackupPath = settings.OriginalBackupFilePath;
            File.Move(tempBackupPath, finalBackupPath, overwrite: true);

            // Falls sich die Dateiendung gegenüber einer früheren Sicherung geändert hat, alte Datei aufräumen
            if (!string.IsNullOrWhiteSpace(previousBackupPath) &&
                !PathsEqual(previousBackupPath, finalBackupPath) &&
                File.Exists(previousBackupPath))
            {
                try
                {
                    File.Delete(previousBackupPath);
                }
                catch
                {
                    // Ignorieren
                }
            }

            settings.HasOriginalBackup = true;
            settings.MonitorId = osState.MonitorId;
            settings.OriginalWasSolidColorOnly = false;
            settings.OriginalBackupFilePath = finalBackupPath;
            settings.OriginalSourceFilePath = osState.WallpaperFilePath;
            settings.OriginalBackupFileHash = copyHash;
            settings.OriginalBackgroundColorR = osState.BackgroundColor.R;
            settings.OriginalBackgroundColorG = osState.BackgroundColor.G;
            settings.OriginalBackgroundColorB = osState.BackgroundColor.B;
            settings.OriginalPositionMode = osState.PositionMode;
            settings.OriginalScreenWidth = osState.ScreenWidth;
            settings.OriginalScreenHeight = osState.ScreenHeight;
        }
        finally
        {
            if (File.Exists(tempBackupPath))
            {
                try
                {
                    File.Delete(tempBackupPath);
                }
                catch
                {
                    // Ignorieren
                }
            }
        }
    }

    /// <summary>
    /// Stellt das gesicherte ursprüngliche Hintergrundbild und dessen Anzeigemodus wieder her (Kapitel 5 & 15).
    /// </summary>
    public RestoreResult RestoreOriginalWallpaper(
        AppSettings settings,
        IWallpaperService wallpaperService,
        SettingsManager settingsManager,
        bool forceRestoreEvenIfExternalChanged = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(wallpaperService);
        ArgumentNullException.ThrowIfNull(settingsManager);

        if (!HasValidOriginalBackup(settings))
        {
            return new RestoreResult(
                RestoreResultStatus.MissingOrInvalidBackup,
                Localizer.T("Backup.Restore.MissingBackup"));
        }

        WallpaperState currentOsState = wallpaperService.GetCurrentState();

        // Besonderer Schutzfall (Kapitel 15):
        // Hat der Benutzer inzwischen selbst ein anderes Windows-Hintergrundbild eingestellt,
        // darf Desktop Sticker dieses nicht ungefragt durch die alte Sicherung ersetzen.
        if (!forceRestoreEvenIfExternalChanged && HasExternalWallpaperChanged(currentOsState, settings))
        {
            return new RestoreResult(
                RestoreResultStatus.ExternalWallpaperChanged,
                Localizer.T("Backup.Restore.ExternalChange"));
        }

        try
        {
            if (settings.OriginalWasSolidColorOnly)
            {
                wallpaperService.ApplySolidColor(settings.OriginalBackgroundColor);

                WallpaperState afterState = wallpaperService.GetCurrentState();
                if (!afterState.IsSolidColorOnly)
                {
                    return new RestoreResult(
                        RestoreResultStatus.VerificationFailed,
                        Localizer.T("Backup.Restore.SolidColorNotConfirmed"));
                }
            }
            else
            {
                wallpaperService.SetBackgroundColor(settings.OriginalBackgroundColor);
                wallpaperService.ApplyWallpaper(
                    settings.MonitorId,
                    settings.OriginalBackupFilePath,
                    settings.OriginalPositionMode);

                WallpaperState afterState = wallpaperService.GetCurrentState();
                if (afterState.IsSolidColorOnly ||
                    (!PathsEqual(afterState.WallpaperFilePath, settings.OriginalBackupFilePath) &&
                     !HasMatchingHash(afterState.WallpaperFilePath, settings.OriginalBackupFileHash)))
                {
                    return new RestoreResult(
                        RestoreResultStatus.VerificationFailed,
                        Localizer.T("Backup.Restore.ImageNotConfirmed"));
                }
            }
        }
        catch (Exception ex)
        {
            return new RestoreResult(
                RestoreResultStatus.VerificationFailed,
                Localizer.T("Backup.Restore.Failed", ex.Message));
        }

        settings.IsStickerActive = false;
        try
        {
            settingsManager.Save(settings);
        }
        catch
        {
            // Das Hintergrundbild wurde bereits erfolgreich wiederhergestellt; ein Schreibfehler der Konfigurationsdatei
            // darf den erfolgreichen Restore-Status nicht zunichtemachen.
        }

        return new RestoreResult(RestoreResultStatus.Success);
    }

    private static bool IsTranscodedWallpaperPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               path.EndsWith(@"\Themes\TranscodedWallpaper", StringComparison.OrdinalIgnoreCase);
    }

    public static bool PathsEqual(string? pathA, string? pathB)
    {
        if (string.IsNullOrWhiteSpace(pathA) || string.IsNullOrWhiteSpace(pathB))
        {
            return false;
        }

        try
        {
            string fullA = Path.GetFullPath(pathA).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullB = Path.GetFullPath(pathB).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(pathA.Trim(), pathB.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool HasMatchingHash(string? filePath, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(expectedHash) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            string actual = AppStoragePaths.ComputeFileSha256(filePath);
            return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
