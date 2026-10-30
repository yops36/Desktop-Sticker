using DesktopSticker.Backup;
using DesktopSticker.Configuration;
using DesktopSticker.Integration;
using DesktopSticker.Localization;
using DesktopSticker.Overlay;
using DesktopSticker.UI;
using DesktopSticker.Wallpaper;

namespace DesktopSticker;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string firstArg = args.Length > 0 ? args[0].Trim() : string.Empty;

        try
        {
            var paths = new AppStoragePaths();
            var settingsManager = new SettingsManager(paths);
            Localizer.InitializeFromSettings(settingsManager);

            // Hintergrund-Hostmodus für das Desktop-Sticker-Overlay (kompatibel mit Wallpaper Engine & Seelen UI)
            if (string.Equals(firstArg, "--overlay", StringComparison.OrdinalIgnoreCase))
            {
                return OverlayHostManager.RunOverlayProcess(settingsManager);
            }

            var wallpaperService = new WindowsWallpaperService();
            var backupManager = new BackupManager(paths);
            var activationManager = new WallpaperActivationManager(paths, wallpaperService, backupManager, settingsManager);
            var contextMenuRegistrar = new ContextMenuRegistrar();
            var autostartRegistrar = new AutostartRegistrar();

            if (string.Equals(firstArg, "--deactivate", StringComparison.OrdinalIgnoreCase))
            {
                return HandleDeactivateCommand(wallpaperService, settingsManager, backupManager, autostartRegistrar);
            }

            if (string.Equals(firstArg, "--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                return HandleUninstallCommand(paths, wallpaperService, settingsManager, backupManager, contextMenuRegistrar);
            }

            if (SetupForm.ShouldRunSetupMode(args))
            {
                LanguageSelectionForm.EnsureLanguageSelected(settingsManager);
                using var setupForm = new SetupForm();
                Application.Run(setupForm);
                return 0;
            }

            using var singleInstance = new SingleInstanceGuard();
            if (!singleInstance.IsFirstInstance)
            {
                SingleInstanceGuard.ActivateExistingInstanceWindow();
                return 0;
            }

            LanguageSelectionForm.EnsureLanguageSelected(settingsManager);

            using var mainForm = new MainForm(
                paths,
                wallpaperService,
                settingsManager,
                backupManager,
                activationManager,
                contextMenuRegistrar,
                autostartRegistrar);

            Application.Run(mainForm);
            return 0;
        }
        catch (Exception ex)
        {
            if (!string.Equals(firstArg, "--overlay", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    Localizer.T("Program.UnexpectedError.Text", ex.Message),
                    Localizer.T("Program.UnexpectedError.Title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            return 1;
        }
    }

    private static int HandleDeactivateCommand(
        IWallpaperService wallpaperService,
        SettingsManager settingsManager,
        BackupManager backupManager,
        AutostartRegistrar autostartRegistrar)
    {
        try
        {
            OverlayHostManager.StopOverlay();
        }
        catch
        {
            // Ignorieren
        }

        autostartRegistrar.Unregister();

        SettingsLoadResult loadResult = settingsManager.Load();
        AppSettings settings = loadResult.Settings;
        WallpaperState currentOs = wallpaperService.GetCurrentState();

        // Falls noch ein älteres generiertes Hintergrundbild in Windows aktiv war, Original wiederherstellen
        // (Funktioniert dank CreateRecoveredSettings() auch dann, wenn settings.json gelöscht wurde, aber Originals\ noch existiert)
        if (backupManager.IsGeneratedWallpaper(currentOs, settings) &&
            backupManager.HasValidOriginalBackup(settings))
        {
            _ = backupManager.RestoreOriginalWallpaper(
                settings,
                wallpaperService,
                settingsManager,
                forceRestoreEvenIfExternalChanged: true);
        }

        if (loadResult.Status != SettingsLoadStatus.FirstLaunch || settings.HasOriginalBackup)
        {
            try
            {
                settings.IsStickerActive = false;
                settingsManager.Save(settings);
            }
            catch
            {
                // Schreibfehler beim Speichern der Deaktivierung darf den Befehl nicht zum Absturz bringen
            }
        }

        return 0;
    }

    private static int HandleUninstallCommand(
        AppStoragePaths paths,
        IWallpaperService wallpaperService,
        SettingsManager settingsManager,
        BackupManager backupManager,
        ContextMenuRegistrar contextMenuRegistrar)
    {
        DialogResult deleteDataChoice = MessageBox.Show(
            Localizer.T("Uninstall.Prepare.Text"),
            Localizer.T("Uninstall.Prepare.Title"),
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (deleteDataChoice == DialogResult.Cancel)
        {
            return 0;
        }

        var coordinator = new UninstallCoordinator(
            paths,
            wallpaperService,
            backupManager,
            settingsManager,
            contextMenuRegistrar);

        UninstallPrepareResult result = coordinator.Execute(
            userRequestedDeleteUserData: deleteDataChoice == DialogResult.Yes);

        SetupForm.RemoveShortcutsAndUninstallEntry();

        if (!string.IsNullOrWhiteSpace(result.WarningMessage))
        {
            MessageBox.Show(
                result.WarningMessage,
                Localizer.T("Uninstall.Warning.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else
        {
            MessageBox.Show(
                Localizer.T("Uninstall.Completed.Text"),
                Localizer.T("Uninstall.Completed.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return 0;
    }
}
