using DesktopSticker.Backup;
using DesktopSticker.Configuration;
using DesktopSticker.Localization;
using DesktopSticker.Overlay;
using DesktopSticker.Wallpaper;

namespace DesktopSticker.Integration;

public sealed record UninstallPrepareResult(
    bool OriginalRestoredOrNotNeeded,
    bool ContextMenuRemoved,
    bool UserDataDeleted,
    string? WarningMessage = null);

/// <summary>
/// Führt die geordnete Deinstallationsvorbereitung gemäß Kapitel 22 durch:
/// 1. Prüfen, ob das erzeugte Sticker-Wallpaper noch aktiv ist.
/// 2. Falls erforderlich und sicher möglich, das Originalhintergrundbild wiederherstellen.
/// 3. Kontextmenüeinträge entfernen.
/// 4. Benutzerdaten und Sicherungen nur nach ausdrücklicher Entscheidung löschen (und niemals,
///    solange die Wiederherstellung des Originals noch aussteht).
/// </summary>
public sealed class UninstallCoordinator
{
    private readonly AppStoragePaths _paths;
    private readonly IWallpaperService _wallpaperService;
    private readonly BackupManager _backupManager;
    private readonly SettingsManager _settingsManager;
    private readonly ContextMenuRegistrar _contextMenuRegistrar;
    private readonly AutostartRegistrar _autostartRegistrar;
    private readonly bool _stopOverlayProcess;

    public UninstallCoordinator(
        AppStoragePaths paths,
        IWallpaperService wallpaperService,
        BackupManager backupManager,
        SettingsManager settingsManager,
        ContextMenuRegistrar contextMenuRegistrar,
        AutostartRegistrar? autostartRegistrar = null,
        bool stopOverlayProcess = true)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _backupManager = backupManager ?? throw new ArgumentNullException(nameof(backupManager));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _contextMenuRegistrar = contextMenuRegistrar ?? throw new ArgumentNullException(nameof(contextMenuRegistrar));
        _autostartRegistrar = autostartRegistrar ?? new AutostartRegistrar();
        _stopOverlayProcess = stopOverlayProcess;
    }

    public UninstallPrepareResult Execute(bool userRequestedDeleteUserData)
    {
        string? warning = null;
        bool originalSafe = true;

        if (_stopOverlayProcess)
        {
            try
            {
                OverlayHostManager.StopOverlay();
            }
            catch
            {
                // Fehler beim Beenden des Overlays darf die Deinstallation nicht abbrechen
            }
        }

        _autostartRegistrar.Unregister();

        try
        {
            SettingsLoadResult loadResult = _settingsManager.Load();
            AppSettings settings = loadResult.Settings;
            WallpaperState currentOsState = _wallpaperService.GetCurrentState();

            // Schritt 1: Prüfen, ob das erzeugte Sticker-Wallpaper noch aktiv ist
            bool stickerWallpaperCurrentlyActive =
                _backupManager.IsGeneratedWallpaper(currentOsState, settings) ||
                (settings.IsStickerActive && !_backupManager.HasExternalWallpaperChanged(currentOsState, settings));

            // Schritt 2: Falls erforderlich und sicher möglich, das Originalhintergrundbild wiederherstellen
            if (stickerWallpaperCurrentlyActive)
            {
                if (_backupManager.HasValidOriginalBackup(settings))
                {
                    RestoreResult restoreRes = _backupManager.RestoreOriginalWallpaper(
                        settings,
                        _wallpaperService,
                        _settingsManager,
                        forceRestoreEvenIfExternalChanged: false);

                    if (restoreRes.Status != RestoreResultStatus.Success)
                    {
                        originalSafe = false;
                        warning = restoreRes.Message ?? Localizer.T("Uninstall.Warn.RestoreFailed");
                    }
                }
                else
                {
                    originalSafe = false;
                    warning = Localizer.T("Uninstall.Warn.NoBackup");
                }
            }
        }
        catch (Exception ex)
        {
            originalSafe = false;
            warning = Localizer.T("Uninstall.Warn.CheckFailed", ex.Message);
        }

        // Schritt 3: Kontextmenüeinträge entfernen (wird garantiert auch nach Fehlern in Schritt 1/2 ausgeführt)
        bool menuRemoved = _contextMenuRegistrar.Unregister();
        if (!menuRemoved)
        {
            warning = CombineWarnings(warning, Localizer.T("Uninstall.Warn.ContextMenu"));
        }

        // Schritt 4: Benutzerdaten und Sicherungen nur nach ausdrücklicher Entscheidung löschen,
        // und NIEMALS wenn die Wiederherstellung des noch aktiven Sticker-Wallpapers fehlgeschlagen ist (Schutzregel Kapitel 22).
        bool dataDeleted = false;
        if (userRequestedDeleteUserData)
        {
            if (originalSafe)
            {
                try
                {
                    if (Directory.Exists(_paths.RootDir))
                    {
                        Directory.Delete(_paths.RootDir, recursive: true);
                    }
                    dataDeleted = true;
                }
                catch (Exception ex)
                {
                    warning = CombineWarnings(warning, Localizer.T("Uninstall.Warn.DeleteDataFailed", ex.Message));
                }
            }
            else
            {
                warning = CombineWarnings(
                    warning,
                    Localizer.T("Uninstall.Warn.BackupKept"));
            }
        }

        return new UninstallPrepareResult(
            OriginalRestoredOrNotNeeded: originalSafe,
            ContextMenuRemoved: menuRemoved,
            UserDataDeleted: dataDeleted,
            WarningMessage: warning);
    }

    private static string CombineWarnings(string? existing, string addition) =>
        string.IsNullOrWhiteSpace(existing) ? addition : $"{existing} {addition}";
}
