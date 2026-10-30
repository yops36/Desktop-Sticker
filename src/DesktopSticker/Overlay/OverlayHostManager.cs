using System.Diagnostics;
using DesktopSticker.Configuration;
using DesktopSticker.Integration;

namespace DesktopSticker.Overlay;

/// <summary>
/// Steuert den leichtgewichtigen Hintergrund-Hostprozess ("--overlay") für das Desktop-Sticker-Widget
/// (inkl. animierter GIF-Unterstützung) über benannte Betriebssystem-Events und einen eigenen Mutex.
/// </summary>
public static class OverlayHostManager
{
    public const string OverlayMutexName = @"Local\DesktopSticker_OverlayInstance_Mutex_v2";
    public const string ReloadEventName = @"Local\DesktopSticker_OverlayReload_Event_v2";
    public const string ShutdownEventName = @"Local\DesktopSticker_OverlayShutdown_Event_v2";
    public const string ReadyEventName = @"Local\DesktopSticker_OverlayReady_Event_v2";

    private const string LegacyV1ShutdownEventName = @"Local\DesktopSticker_OverlayShutdown_Event_v1";

    /// <summary>
    /// Prüft, ob derzeit eine aktive Overlay-Widget-Instanz läuft.
    /// </summary>
    public static bool IsOverlayRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(OverlayMutexName, out Mutex? existingMutex))
            {
                return false;
            }

            using (existingMutex)
            {
                try
                {
                    if (existingMutex.WaitOne(0, exitContext: false))
                    {
                        existingMutex.ReleaseMutex();
                        return false;
                    }
                    return true;
                }
                catch (AbandonedMutexException)
                {
                    existingMutex.ReleaseMutex();
                    return false;
                }
            }
        }
        catch
        {
            return false;
        }
    }

    private static void StopLegacyV1OverlayIfRunning()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(LegacyV1ShutdownEventName, out EventWaitHandle? legacyShutdown))
            {
                using (legacyShutdown)
                {
                    legacyShutdown.Set();
                }
            }
        }
        catch
        {
            // Ignorieren
        }
    }

    /// <summary>
    /// Startet den Overlay-Hostprozess neu oder weist eine bereits laufende Instanz an,
    /// die geänderten Sticker-Einstellungen sofort neu zu laden.
    /// </summary>
    public static bool StartOrReloadOverlay(string? executablePath = null)
    {
        StopLegacyV1OverlayIfRunning();

        using var reloadEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ReloadEventName);
        using var shutdownEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownEventName);
        using var readyEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEventName);

        shutdownEvent.Reset();
        readyEvent.Reset();

        if (IsOverlayRunning())
        {
            reloadEvent.Set();
            if (readyEvent.WaitOne(TimeSpan.FromMilliseconds(2500)))
            {
                return true;
            }
        }

        string? exePath = string.IsNullOrWhiteSpace(executablePath)
            ? Environment.ProcessPath
            : executablePath;

        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--overlay",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            };

            using Process? started = Process.Start(psi);
            if (started is null)
            {
                return false;
            }

            _ = readyEvent.WaitOne(TimeSpan.FromMilliseconds(3500));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Beendet eine laufende Overlay-Widget-Instanz geordnet.
    /// </summary>
    public static void StopOverlay()
    {
        StopLegacyV1OverlayIfRunning();

        try
        {
            using var shutdownEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownEventName);
            shutdownEvent.Set();

            // Kurz warten, bis der Overlay-Mutex freigegeben wurde
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1500 && IsOverlayRunning())
            {
                Thread.Sleep(30);
            }
        }
        catch
        {
            // Ignorieren
        }
    }

    /// <summary>
    /// Führt die Nachrichtenschleife des "--overlay"-Prozesses aus.
    /// </summary>
    public static int RunOverlayProcess(SettingsManager settingsManager)
    {
        ArgumentNullException.ThrowIfNull(settingsManager);

        StopLegacyV1OverlayIfRunning();

        using var overlayGuard = new SingleInstanceGuard(OverlayMutexName);
        using var reloadEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ReloadEventName);
        using var shutdownEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownEventName);
        using var readyEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEventName);

        if (!overlayGuard.IsFirstInstance)
        {
            reloadEvent.Set();
            return 0;
        }

        shutdownEvent.Reset();

        SettingsLoadResult loadResult = settingsManager.Load();
        AppSettings settings = loadResult.Settings;

        if (!settings.IsStickerActive ||
            string.IsNullOrWhiteSpace(settings.StickerFilePath) ||
            !File.Exists(settings.StickerFilePath))
        {
            readyEvent.Set();
            return 0;
        }

        using var overlayForm = new StickerOverlayForm(settingsManager, reloadEvent, shutdownEvent, readyEvent);
        Application.Run(overlayForm);
        return 0;
    }
}
