using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using DesktopSticker.Backup;
using DesktopSticker.Configuration;
using DesktopSticker.Imaging;
using DesktopSticker.Integration;
using DesktopSticker.UI;
using DesktopSticker.Wallpaper;
using Microsoft.Win32;

namespace DesktopSticker.Tests;

internal sealed class InMemoryRegistryStore : IRegistryStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    public bool ThrowOnDelete { get; set; }
    public bool ThrowOnRead { get; set; }

    public void SetStringValue(string subKeyPath, string? valueName, string value)
    {
        _values[$"{subKeyPath}::{valueName ?? "(Default)"}"] = value;
    }

    public string? GetStringValue(string subKeyPath, string? valueName)
    {
        if (ThrowOnRead)
        {
            throw new UnauthorizedAccessException("Simulierter blockierter Registry-Lesezugriff.");
        }
        return _values.TryGetValue($"{subKeyPath}::{valueName ?? "(Default)"}", out string? val) ? val : null;
    }

    public void DeleteSubKeyTree(string subKeyPath)
    {
        if (ThrowOnDelete)
        {
            throw new UnauthorizedAccessException("Simulierter blockierter Registry-Löschzugriff.");
        }
        string prefix = subKeyPath;
        foreach (string key in _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _values.Remove(key);
        }
    }
}

internal sealed class MockWallpaperService : IWallpaperService
{
    public WallpaperState CurrentState { get; set; } = new()
    {
        MonitorId = "MON_0",
        ConnectedMonitorCount = 1,
        ScreenWidth = 1920,
        ScreenHeight = 1080,
        PositionMode = WallpaperPositionMode.Fit,
        BackgroundColor = Color.FromArgb(255, 10, 20, 30),
        IsSolidColorOnly = false,
        FileExistsAndReadable = true,
        IsSupportedConfiguration = true
    };

    public WallpaperState GetCurrentState() => CurrentState;

    public void ApplyWallpaper(string? monitorId, string imageFilePath, WallpaperPositionMode positionMode)
    {
        CurrentState = CurrentState with
        {
            MonitorId = monitorId ?? CurrentState.MonitorId,
            WallpaperFilePath = imageFilePath,
            PositionMode = positionMode,
            IsSolidColorOnly = false,
            FileExistsAndReadable = File.Exists(imageFilePath)
        };
    }

    public void ApplySolidColor(Color backgroundColor)
    {
        CurrentState = CurrentState with
        {
            WallpaperFilePath = string.Empty,
            BackgroundColor = backgroundColor,
            IsSolidColorOnly = true,
            FileExistsAndReadable = true
        };
    }

    public void SetBackgroundColor(Color backgroundColor)
    {
        CurrentState = CurrentState with
        {
            BackgroundColor = backgroundColor
        };
    }
}

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.WriteLine("=== DesktopSticker Test-Suite ===");
        try
        {
            RunPhase2Tests();
            RunPhase3Tests();
            RunPhase4To7Tests();
            RunPhase8To10Tests();
            RunPhase11To16Tests();
            RunGifTests();
            Console.WriteLine("ALLE TESTS ERFOLGREICH BESTANDEN.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TEST FEHLGESCHLAGEN: {ex}");
            return 1;
        }
    }

    private static void RunPhase2Tests()
    {
        Console.WriteLine("[Phase 2] Teste Windows-Wallpaper-Erkennung...");

        Color sampleColor = Color.FromArgb(255, 18, 52, 86);
        uint colorRef = WindowsWallpaperService.ColorToColorRef(sampleColor);
        Color roundTrip = WindowsWallpaperService.ColorFromColorRef(colorRef);
        Assert(roundTrip.R == 18 && roundTrip.G == 52 && roundTrip.B == 86, "COLORREF Roundtrip fehlgeschlagen.");

        var (okStatic, _) = WindowsWallpaperService.EvaluateSupport(
            screenCount: 1, screenWidth: 1920, screenHeight: 1080,
            isPolicyRestricted: false, isSlideshow: false, isSpotlight: false,
            posMode: WallpaperPositionMode.Fill, isSolidColorOnly: false,
            wallpaperPath: @"C:\Windows\Web\Wallpaper\Windows\img0.jpg", fileExistsAndReadable: true);
        Assert(okStatic, "Statisches Einzelmonitor-Wallpaper muss unterstützt sein.");

        var (okSolid, _) = WindowsWallpaperService.EvaluateSupport(
            screenCount: 1, screenWidth: 2560, screenHeight: 1440,
            isPolicyRestricted: false, isSlideshow: false, isSpotlight: false,
            posMode: WallpaperPositionMode.Center, isSolidColorOnly: true,
            wallpaperPath: "", fileExistsAndReadable: true);
        Assert(okSolid, "Einfarbiger Hintergrund muss unterstützt sein.");

        var (okMultiMon, reasonMultiMon) = WindowsWallpaperService.EvaluateSupport(
            screenCount: 2, screenWidth: 1920, screenHeight: 1080,
            isPolicyRestricted: false, isSlideshow: false, isSpotlight: false,
            posMode: WallpaperPositionMode.Fill, isSolidColorOnly: false,
            wallpaperPath: @"C:\wallpaper.jpg", fileExistsAndReadable: true);
        Assert(!okMultiMon && !string.IsNullOrEmpty(reasonMultiMon), "Mehrere Monitore müssen in v1.0 abgelehnt werden.");

        var (okSlideshow, reasonSlideshow) = WindowsWallpaperService.EvaluateSupport(
            screenCount: 1, screenWidth: 1920, screenHeight: 1080,
            isPolicyRestricted: false, isSlideshow: true, isSpotlight: false,
            posMode: WallpaperPositionMode.Fill, isSolidColorOnly: false,
            wallpaperPath: @"C:\wallpaper.jpg", fileExistsAndReadable: true);
        Assert(!okSlideshow && !string.IsNullOrEmpty(reasonSlideshow), "Diashow muss in v1.0 abgelehnt werden.");

        var (okSpotlight, reasonSpotlight) = WindowsWallpaperService.EvaluateSupport(
            screenCount: 1, screenWidth: 1920, screenHeight: 1080,
            isPolicyRestricted: false, isSlideshow: false, isSpotlight: true,
            posMode: WallpaperPositionMode.Fill, isSolidColorOnly: false,
            wallpaperPath: @"C:\spotlight.jpg", fileExistsAndReadable: true);
        Assert(!okSpotlight && !string.IsNullOrEmpty(reasonSpotlight), "Spotlight muss in v1.0 abgelehnt werden.");

        var (okPolicy, reasonPolicy) = WindowsWallpaperService.EvaluateSupport(
            screenCount: 1, screenWidth: 1920, screenHeight: 1080,
            isPolicyRestricted: true, isSlideshow: false, isSpotlight: false,
            posMode: WallpaperPositionMode.Fill, isSolidColorOnly: false,
            wallpaperPath: @"C:\wallpaper.jpg", fileExistsAndReadable: true);
        Assert(!okPolicy && !string.IsNullOrEmpty(reasonPolicy), "Richtliniensperre muss erkannt werden.");

        var service = new WindowsWallpaperService();
        WallpaperState state = service.GetCurrentState();
        Assert(state.ScreenWidth > 0 && state.ScreenHeight > 0, "Bildschirmauflösung muss größer als 0 sein.");
        Assert(state.ConnectedMonitorCount >= 1, "Mindestens ein Monitor muss erkannt werden.");
        Console.WriteLine("[Phase 2] Erfolgreich geprüft.");
    }

    private static void RunPhase3Tests()
    {
        Console.WriteLine("[Phase 3] Teste Originalhintergrund-Sicherung und Schutzregeln...");

        string testDir = Path.Combine(Path.GetTempPath(), $"DesktopSticker_TestPhase3_{Guid.NewGuid():N}");
        try
        {
            var paths = new AppStoragePaths(testDir);
            paths.EnsureDirectoriesAndWriteAccess();
            var settingsMgr = new SettingsManager(paths);
            var backupMgr = new BackupManager(paths);

            // Erzeuge ein künstliches Original-Hintergrundbild außerhalb des App-Ordners
            string externalDir = Path.Combine(testDir, "ExternalUserPictures");
            Directory.CreateDirectory(externalDir);
            string origWallpaperPath = Path.Combine(externalDir, "my_photo.png");
            CreateTestBitmap(origWallpaperPath, 800, 600, Color.CornflowerBlue);

            var mockService = new MockWallpaperService();
            mockService.CurrentState = mockService.CurrentState with
            {
                WallpaperFilePath = origWallpaperPath,
                PositionMode = WallpaperPositionMode.Fit,
                BackgroundColor = Color.FromArgb(255, 12, 34, 56),
                ScreenWidth = 1920,
                ScreenHeight = 1080
            };

            var settings = new AppSettings();
            backupMgr.CreateOrUpdateOriginalBackup(mockService.GetCurrentState(), settings);
            settingsMgr.Save(settings);

            Assert(settings.HasOriginalBackup, "HasOriginalBackup muss true sein.");
            Assert(File.Exists(settings.OriginalBackupFilePath), "Sicherungskopie muss im Originals-Ordner existieren.");
            Assert(settings.OriginalPositionMode == WallpaperPositionMode.Fit, "Original-Positionierungsart muss gesichert werden.");
            Assert(settings.OriginalBackgroundColor.R == 12 && settings.OriginalBackgroundColor.G == 34 && settings.OriginalBackgroundColor.B == 56, "Original-Hintergrundfarbe muss gesichert werden.");

            string savedOriginalHash = settings.OriginalBackupFileHash;

            // Simuliere ein von Desktop Sticker generiertes Hintergrundbild
            string fakeGeneratedPath = Path.Combine(paths.GeneratedDir, "DesktopSticker_Generated_Wallpaper.bmp");
            CreateTestBitmap(fakeGeneratedPath, 1920, 1080, Color.Crimson);
            settings.CurrentGeneratedWallpaperPath = fakeGeneratedPath;
            settings.CurrentGeneratedWallpaperHash = AppStoragePaths.ComputeFileSha256(fakeGeneratedPath);
            settings.IsStickerActive = true;
            settingsMgr.Save(settings);

            mockService.ApplyWallpaper("MON_0", fakeGeneratedPath, WallpaperPositionMode.Fill);

            // Entscheidende Schutzregel prüfen (Kapitel 5):
            // Beim erneuten Öffnen darf das generierte Bild NIEMALS als neues Original gesichert werden!
            Assert(backupMgr.IsGeneratedWallpaper(mockService.GetCurrentState(), settings), "Generiertes Wallpaper muss als solches erkannt werden.");
            backupMgr.CreateOrUpdateOriginalBackup(mockService.GetCurrentState(), settings);
            Assert(settings.OriginalBackupFileHash == savedOriginalHash, "Schutzregel: Original-Backup darf niemals durch ein generiertes Wallpaper überschrieben werden!");
            Assert(settings.OriginalPositionMode == WallpaperPositionMode.Fit, "Schutzregel: Original-Positionierungsart muss erhalten bleiben!");

            // TranscodedWallpaper-Schutz bei aktivem Sticker prüfen
            WallpaperState transcodedState = mockService.GetCurrentState() with
            {
                WallpaperFilePath = Path.Combine(externalDir, @"Themes\TranscodedWallpaper")
            };
            Directory.CreateDirectory(Path.GetDirectoryName(transcodedState.WallpaperFilePath)!);
            CreateTestBitmap(transcodedState.WallpaperFilePath, 1920, 1080, Color.Crimson);
            backupMgr.CreateOrUpdateOriginalBackup(transcodedState, settings);
            Assert(settings.OriginalBackupFileHash == savedOriginalHash, "Schutzregel: TranscodedWallpaper darf bei aktivem Sticker niemals das Original-Backup überschreiben!");

            // Wiederherstellung prüfen (Kapitel 5 & 15)
            RestoreResult restoreRes = backupMgr.RestoreOriginalWallpaper(settings, mockService, settingsMgr);
            Assert(restoreRes.Status == RestoreResultStatus.Success, $"Wiederherstellung fehlgeschlagen: {restoreRes.Message}");
            Assert(!settings.IsStickerActive, "Nach dem Deaktivieren muss IsStickerActive false sein.");
            Assert(mockService.GetCurrentState().PositionMode == WallpaperPositionMode.Fit, "Nach der Wiederherstellung muss der ursprüngliche Anzeigemodus (Fit) aktiv sein.");
            Assert(BackupManager.PathsEqual(mockService.GetCurrentState().WallpaperFilePath, settings.OriginalBackupFilePath), "Nach der Wiederherstellung muss das gesicherte Originalbild aktiv sein.");

            // Doppelsicherungs-Schutz prüfen: Wenn das aktive Wallpaper bereits auf OriginalBackupFilePath zeigt,
            // muss OriginalSourceFilePath beim erneuten CreateOrUpdateOriginalBackup erhalten bleiben.
            backupMgr.CreateOrUpdateOriginalBackup(mockService.GetCurrentState(), settings);
            Assert(BackupManager.PathsEqual(settings.OriginalSourceFilePath, origWallpaperPath),
                "OriginalSourceFilePath darf beim erneuten Öffnen nach Wiederherstellung nicht mit OriginalBackupFilePath überschrieben werden.");

            // Schutzfall prüfen: Benutzer ändert außerhalb von Desktop Sticker sein Wallpaper (Kapitel 15 & 18.1)
            settings.IsStickerActive = true;
            string thirdPartyWallpaper = Path.Combine(externalDir, "user_new_wallpaper.png");
            CreateTestBitmap(thirdPartyWallpaper, 1920, 1080, Color.ForestGreen);
            mockService.ApplyWallpaper("MON_0", thirdPartyWallpaper, WallpaperPositionMode.Stretch);

            Assert(backupMgr.HasExternalWallpaperChanged(mockService.GetCurrentState(), settings), "Externe Wallpaper-Änderung muss erkannt werden.");
            RestoreResult blockedRestore = backupMgr.RestoreOriginalWallpaper(settings, mockService, settingsMgr, forceRestoreEvenIfExternalChanged: false);
            Assert(blockedRestore.Status == RestoreResultStatus.ExternalWallpaperChanged, "Wiederherstellung darf ein extern geändertes Wallpaper nicht ungefragt überschreiben.");
            Assert(BackupManager.PathsEqual(mockService.GetCurrentState().WallpaperFilePath, thirdPartyWallpaper), "Externes Wallpaper muss unangetastet bleiben.");

            Console.WriteLine("[Phase 3] Erfolgreich geprüft.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(testDir))
                {
                    Directory.Delete(testDir, recursive: true);
                }
            }
            catch
            {
                // Ignorieren
            }
        }
    }

    private static void RunPhase4To7Tests()
    {
        Console.WriteLine("[Phase 4-7] Teste Bildladen, EXIF-Orientierung, Größe, Position und Eckenradius...");

        string testDir = Path.Combine(Path.GetTempPath(), $"DesktopSticker_TestPhase4_7_{Guid.NewGuid():N}");
        try
        {
            var paths = new AppStoragePaths(testDir);
            paths.EnsureDirectoriesAndWriteAccess();

            string userDir = Path.Combine(testDir, "UserDownloads");
            Directory.CreateDirectory(userDir);

            // 1. Transparentes PNG erstellen und importieren (Phase 4)
            string pngPath = Path.Combine(userDir, "sticker_trans.png");
            using (var rawPng = new Bitmap(200, 100, PixelFormat.Format32bppArgb))
            {
                using Graphics g = Graphics.FromImage(rawPng);
                g.Clear(Color.Transparent);
                using var brush = new SolidBrush(Color.FromArgb(255, 220, 40, 60));
                g.FillRectangle(brush, 20, 10, 160, 80);
                rawPng.Save(pngPath, ImageFormat.Png);
            }

            using LoadedStickerImage loadedPng = StickerImageLoader.ImportFromUserFile(pngPath, paths);
            // Quelldatei im Download-Ordner löschen -> lokale Kopie muss weiterhin funktionieren (Kapitel 6)
            File.Delete(pngPath);
            Assert(File.Exists(loadedPng.LocalFilePath), "Lokale Kopie im Stickers-Ordner muss existieren.");
            Assert(loadedPng.Width == 200 && loadedPng.Height == 100, "PNG-Abmessungen müssen 200x100 sein.");
            Assert(loadedPng.Bitmap.GetPixel(0, 0).A == 0, "Transparenter PNG-Rand muss Alpha=0 behalten.");
            Assert(loadedPng.Bitmap.GetPixel(100, 50).A == 255, "Deckender PNG-Kern muss Alpha=255 behalten.");

            // 2. JPG mit EXIF-Orientierung 6 (90° im Uhrzeigersinn) prüfen (Kapitel 6.4)
            string jpgPath = Path.Combine(userDir, "rotated_photo.jpg");
            using (var rawJpg = new Bitmap(300, 150, PixelFormat.Format24bppRgb))
            {
                using Graphics g = Graphics.FromImage(rawJpg);
                g.Clear(Color.Navy);
                PropertyItem exifProp = (PropertyItem)RuntimeHelpers.GetUninitializedObject(typeof(PropertyItem));
                exifProp.Id = 0x0112;
                exifProp.Type = 3; // SHORT
                exifProp.Len = 2;
                exifProp.Value = BitConverter.GetBytes((ushort)6);
                rawJpg.SetPropertyItem(exifProp);
                rawJpg.Save(jpgPath, ImageFormat.Jpeg);
            }

            using LoadedStickerImage loadedJpg = StickerImageLoader.ImportFromUserFile(jpgPath, paths);
            Assert(loadedJpg.Width == 150 && loadedJpg.Height == 300, $"EXIF-Orientierung 6 muss 300x150 auf 150x300 drehen (Aktuell: {loadedJpg.Width}x{loadedJpg.Height}).");

            // 3. Beschädigte Datei muss kontrolliert abgelehnt werden (Kapitel 6 & 21)
            string corruptPath = Path.Combine(userDir, "broken.png");
            File.WriteAllBytes(corruptPath, new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF });
            bool threwOnCorrupt = false;
            try
            {
                using var _ = StickerImageLoader.ImportFromUserFile(corruptPath, paths);
            }
            catch (InvalidDataException)
            {
                threwOnCorrupt = true;
            }
            Assert(threwOnCorrupt, "Beschädigte Bilddatei muss mit InvalidDataException abgelehnt werden.");

            // 4. Proportionale Größe und Positionierung prüfen (Phasen 5 & 6)
            var placementCenter = StickerGeometry.CalculatePlacement(
                stickerOriginalWidth: 200,
                stickerOriginalHeight: 100,
                targetSurfaceWidth: 1920,
                targetSurfaceHeight: 1080,
                requestedRelativeWidth: 0.25, // 480 px Breite -> 240 px Höhe
                requestedRelativeX: 0.50,
                requestedRelativeY: 0.50);

            Assert(placementCenter.Width == 480 && placementCenter.Height == 240, $"Erwartet 480x240, erhalten {placementCenter.Width}x{placementCenter.Height}.");
            Assert(placementCenter.X == 960 && placementCenter.Y == 540, "Position in der Bildschirmmitte fehlerhaft.");

            // Randbegrenzung (Rechts/Unten) prüfen
            var placementBottomRight = StickerGeometry.CalculatePlacement(
                stickerOriginalWidth: 200,
                stickerOriginalHeight: 100,
                targetSurfaceWidth: 1920,
                targetSurfaceHeight: 1080,
                requestedRelativeWidth: 0.25,
                requestedRelativeX: 0.99,
                requestedRelativeY: 0.99);

            Assert(placementBottomRight.X + placementBottomRight.Width <= 1920, "Sticker darf rechts nicht über den Bildschirmrand ragen.");
            Assert(placementBottomRight.Y + placementBottomRight.Height <= 1080, "Sticker darf unten nicht über den Bildschirmrand ragen.");
            Assert(placementBottomRight.X == 1920 - 480 && placementBottomRight.Y == 1080 - 240, "Sticker muss exakt an der unteren rechten Ecke anliegen.");

            // 5. Eckenradius und Transparenz prüfen (Phase 7)
            using var rectJpg = RoundedCornerProcessor.CreateScaledAndRoundedSticker(loadedJpg.Bitmap, 100, 200, cornerRadiusPercent: 0.0);
            Assert(rectJpg.GetPixel(0, 0).A == 255, "Bei Eckenradius 0 müssen alle Ecken vollständig deckend bleiben.");

            using var roundedJpg = RoundedCornerProcessor.CreateScaledAndRoundedSticker(loadedJpg.Bitmap, 100, 200, cornerRadiusPercent: 80.0);
            Assert(roundedJpg.GetPixel(0, 0).A == 0, "Abgeschnittene linke obere Ecke muss bei Radius > 0 transparent sein.");
            Assert(roundedJpg.GetPixel(99, 0).A == 0, "Abgeschnittene rechte obere Ecke muss bei Radius > 0 transparent sein.");
            Assert(roundedJpg.GetPixel(0, 199).A == 0, "Abgeschnittene linke untere Ecke muss bei Radius > 0 transparent sein.");
            Assert(roundedJpg.GetPixel(99, 199).A == 0, "Abgeschnittene rechte untere Ecke muss bei Radius > 0 transparent sein.");
            Assert(roundedJpg.GetPixel(50, 100).A == 255, "Bildmitte muss deckend bleiben.");

            // Transparentes PNG mit Eckenradius: bereits transparente Bereiche müssen transparent bleiben
            using var roundedPng = RoundedCornerProcessor.CreateScaledAndRoundedSticker(loadedPng.Bitmap, 200, 100, cornerRadiusPercent: 50.0);
            Assert(roundedPng.GetPixel(100, 2).A == 0, "Bereits transparenter oberer PNG-Rand muss auch mit Eckenradius transparent bleiben.");
            Assert(roundedPng.GetPixel(100, 50).A == 255, "Deckender PNG-Mittelbereich muss sichtbar bleiben.");

            Console.WriteLine("[Phase 4-7] Erfolgreich geprüft.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(testDir))
                {
                    Directory.Delete(testDir, recursive: true);
                }
            }
            catch
            {
                // Ignorieren
            }
        }
    }

    private static void RunPhase8To10Tests()
    {
        Console.WriteLine("[Phase 8-10] Teste Wallpaper-Komposition, Aktivierung und Einstellungs-Persistenz...");

        string testDir = Path.Combine(Path.GetTempPath(), $"DesktopSticker_TestPhase8_10_{Guid.NewGuid():N}");
        try
        {
            var paths = new AppStoragePaths(testDir);
            paths.EnsureDirectoriesAndWriteAccess();
            var settingsMgr = new SettingsManager(paths);
            var backupMgr = new BackupManager(paths);
            var mockService = new MockWallpaperService();
            var activator = new WallpaperActivationManager(paths, mockService, backupMgr, settingsMgr);

            // 1. Alle Anzeigemodi in RenderBackgroundSurface prüfen (Kapitel 10)
            Color bgCol = Color.FromArgb(255, 15, 25, 35);
            using var smallOrig = new Bitmap(100, 100, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(smallOrig))
            {
                g.Clear(Color.FromArgb(255, 200, 100, 50));
            }

            // Center: 100x100 mittig auf 400x300 -> (0,0) ist Hintergrundfarbe, (200,150) ist Bildfarbe
            using (Bitmap centerSurface = WallpaperComposer.RenderBackgroundSurface(smallOrig, false, bgCol, WallpaperPositionMode.Center, 400, 300))
            {
                Color corner = centerSurface.GetPixel(5, 5);
                Color mid = centerSurface.GetPixel(200, 150);
                Assert(corner.R == 15 && corner.G == 25 && corner.B == 35, "Center-Modus: Außenbereich muss Hintergrundfarbe zeigen.");
                Assert(mid.R == 200 && mid.G == 100 && mid.B == 50, "Center-Modus: Bildmitte muss Originalbild zeigen.");
            }

            // Fit: 100x100 auf 400x200 -> skaliert auf 200x200 mittig -> links (10,100) Hintergrundfarbe, Mitte (200,100) Bildfarbe
            using (Bitmap fitSurface = WallpaperComposer.RenderBackgroundSurface(smallOrig, false, bgCol, WallpaperPositionMode.Fit, 400, 200))
            {
                Color leftBar = fitSurface.GetPixel(10, 100);
                Color mid = fitSurface.GetPixel(200, 100);
                Assert(leftBar.R == 15 && leftBar.G == 25 && leftBar.B == 35, "Fit-Modus: Randbalken muss Hintergrundfarbe zeigen.");
                Assert(mid.R == 200 && mid.G == 100 && mid.B == 50, "Fit-Modus: Mitte muss Originalbild zeigen.");
            }

            // Tile: 100x100 gekachelt auf 400x300 -> (350, 250) muss Bildfarbe sein
            using (Bitmap tileSurface = WallpaperComposer.RenderBackgroundSurface(smallOrig, false, bgCol, WallpaperPositionMode.Tile, 400, 300))
            {
                Color farCorner = tileSurface.GetPixel(350, 250);
                Assert(farCorner.R == 200 && farCorner.G == 100 && farCorner.B == 50, "Tile-Modus: Kachelung muss gesamte Fläche füllen.");
            }

            // SolidColor: Nur Hintergrundfarbe
            using (Bitmap solidSurface = WallpaperComposer.RenderBackgroundSurface(null, true, bgCol, WallpaperPositionMode.Fill, 400, 300))
            {
                Color mid = solidSurface.GetPixel(200, 150);
                Assert(mid.R == 15 && mid.G == 25 && mid.B == 35, "Einfarbiger Modus muss Hintergrundfarbe auf gesamter Fläche zeigen.");
            }

            // 2. Vollständige Komposition und Aktivierung (Phasen 8 & 9)
            string extDir = Path.Combine(testDir, "External");
            Directory.CreateDirectory(extDir);
            string origFile = Path.Combine(extDir, "desktop_bg.png");
            CreateTestBitmap(origFile, 1000, 500, Color.FromArgb(255, 30, 60, 90));

            string stickerFile = Path.Combine(extDir, "my_sticker.png");
            using (var stBmp = new Bitmap(100, 100, PixelFormat.Format32bppArgb))
            {
                using Graphics g = Graphics.FromImage(stBmp);
                g.Clear(Color.Transparent); // Rand transparent
                using var b = new SolidBrush(Color.FromArgb(255, 250, 200, 10));
                g.FillRectangle(b, 10, 10, 80, 80); // Kern gelb
                stBmp.Save(stickerFile, ImageFormat.Png);
            }

            mockService.CurrentState = new WallpaperState
            {
                MonitorId = "MON_0",
                ConnectedMonitorCount = 1,
                ScreenWidth = 1000,
                ScreenHeight = 500,
                WallpaperFilePath = origFile,
                PositionMode = WallpaperPositionMode.Fill,
                BackgroundColor = Color.Black,
                IsSolidColorOnly = false,
                FileExistsAndReadable = true,
                IsSupportedConfiguration = true
            };

            WallpaperState sessionStart = mockService.GetCurrentState();
            var settings = new AppSettings();
            backupMgr.CreateOrUpdateOriginalBackup(sessionStart, settings);

            using LoadedStickerImage loadedSticker = StickerImageLoader.ImportFromUserFile(stickerFile, paths);
            using Bitmap origLoaded = StickerImageLoader.LoadAndNormalizeBitmap(settings.OriginalBackupFilePath);

            settings.StickerFilePath = loadedSticker.LocalFilePath;
            settings.RelativeWidth = 0.10; // 100 px breit, 100 px hoch
            settings.RelativeX = 0.20;     // X = 200
            settings.RelativeY = 0.20;     // Y = 100
            settings.CornerRadiusPercent = 0.0;

            ActivationResult actRes1 = activator.ComposeAndActivate(sessionStart, settings, origLoaded, loadedSticker.Bitmap);
            Assert(actRes1.Status == ActivationStatus.Success, $"Erste Aktivierung fehlgeschlagen: {actRes1.ErrorMessage}");
            Assert(settings.IsStickerActive, "IsStickerActive muss nach Aktivierung true sein.");
            Assert(File.Exists(settings.CurrentGeneratedWallpaperPath), "Generierte BMP-Datei muss existieren.");

            // Prüfe Pixel im generierten BMP-Wallpaper
            using (Bitmap genBmp1 = StickerImageLoader.LoadAndNormalizeBitmap(settings.CurrentGeneratedWallpaperPath))
            {
                Assert(genBmp1.Width == 1000 && genBmp1.Height == 500, "Generiertes Bild muss exakt Monitorauflösung haben.");
                // Transparenter Rand des Stickers bei (202, 102) muss Originalhintergrund (30, 60, 90) zeigen!
                Color transparentBorderPx = genBmp1.GetPixel(202, 102);
                Assert(transparentBorderPx.R == 30 && transparentBorderPx.G == 60 && transparentBorderPx.B == 90,
                    "Transparenter Bereich des Stickers muss Originalhintergrund ohne schwarze/weiße Ränder zeigen.");

                // Gelber Kern des Stickers bei (250, 150) muss (250, 200, 10) zeigen!
                Color stickerCorePx = genBmp1.GetPixel(250, 150);
                Assert(stickerCorePx.R == 250 && stickerCorePx.G == 200 && stickerCorePx.B == 10,
                    "Sticker-Kern muss an den erwarteten Koordinaten sichtbar sein.");
            }

            // 3. Zweite Bearbeitung (Verschieben des Stickers auf X=600, Y=300): Kein doppelter Sticker! (Kapitel 10, 14, 21)
            WallpaperState sessionStart2 = mockService.GetCurrentState();
            settings.RelativeX = 0.60; // X = 600
            settings.RelativeY = 0.60; // Y = 300

            ActivationResult actRes2 = activator.ComposeAndActivate(sessionStart2, settings, origLoaded, loadedSticker.Bitmap);
            Assert(actRes2.Status == ActivationStatus.Success, $"Zweite Aktivierung fehlgeschlagen: {actRes2.ErrorMessage}");

            using (Bitmap genBmp2 = StickerImageLoader.LoadAndNormalizeBitmap(settings.CurrentGeneratedWallpaperPath))
            {
                // Alte Position (250, 150) muss wieder reiner Originalhintergrund (30, 60, 90) sein!
                Color oldPosPx = genBmp2.GetPixel(250, 150);
                Assert(oldPosPx.R == 30 && oldPosPx.G == 60 && oldPosPx.B == 90,
                    "Nach Verschieben darf an der alten Position kein alter Sticker mehr sichtbar sein (kein doppelter Sticker).");

                // Neue Position (650, 350) muss den Sticker zeigen!
                Color newPosPx = genBmp2.GetPixel(650, 350);
                Assert(newPosPx.R == 250 && newPosPx.G == 200 && newPosPx.B == 10,
                    "Sticker muss an der neuen Position sichtbar sein.");
            }

            // 4. Erkennung einer Wallpaper-Änderung WÄHREND der geöffneten Bearbeitung (Kapitel 11.3 & 21)
            WallpaperState sessionStart3 = mockService.GetCurrentState();
            mockService.ApplySolidColor(Color.Purple); // Externe Änderung während das Fenster offen ist
            ActivationResult actRes3 = activator.ComposeAndActivate(sessionStart3, settings, origLoaded, loadedSticker.Bitmap);
            Assert(actRes3.Status == ActivationStatus.WallpaperChangedDuringEditing,
                "Während der Bearbeitung geändertes Wallpaper muss vor dem Überschreiben erkannt werden.");

            // 5. Einstellungs-Laden und Verhalten bei beschädigter JSON-Datei prüfen (Phase 10 / Kapitel 12 & 21)
            SettingsLoadResult loadedOk = settingsMgr.Load();
            Assert(loadedOk.Status == SettingsLoadStatus.LoadedSuccessfully, "Gespeicherte Einstellungen müssen erfolgreich geladen werden.");
            Assert(Math.Abs(loadedOk.Settings.RelativeX - 0.60) < 0.001, "Geladene Position X muss übereinstimmen.");

            File.WriteAllText(paths.SettingsFilePath, "{ invalid json ");
            SettingsLoadResult loadedCorrupt = settingsMgr.Load();
            Assert(loadedCorrupt.Status == SettingsLoadStatus.CorruptedOrIncomplete, "Beschädigte Konfigurationsdatei muss erkannt werden.");
            Assert(loadedCorrupt.Settings.HasOriginalBackup, "Sichere Wiederherstellung muss vorhandenes Original-Backup im Originals-Ordner finden.");

            Console.WriteLine("[Phase 8-10] Erfolgreich geprüft.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(testDir))
                {
                    Directory.Delete(testDir, recursive: true);
                }
            }
            catch
            {
                // Ignorieren
            }
        }
    }

    private static void RunPhase11To16Tests()
    {
        Console.WriteLine("[Phase 11-16] Teste UI-Vorschau, Ersetzen, Deaktivieren, Kontextmenü, Single-Instance und Deinstallation...");

        string testDir = Path.Combine(Path.GetTempPath(), $"DesktopSticker_TestPhase11_16_{Guid.NewGuid():N}");
        string testRegKey = $@"Software\DesktopSticker_AutomatedTest_{Guid.NewGuid():N}\Shell";
        try
        {
            var paths = new AppStoragePaths(testDir);
            paths.EnsureDirectoriesAndWriteAccess();
            var settingsMgr = new SettingsManager(paths);
            var backupMgr = new BackupManager(paths);
            var mockService = new MockWallpaperService();
            var activator = new WallpaperActivationManager(paths, mockService, backupMgr, settingsMgr);

            string extDir = Path.Combine(testDir, "External");
            Directory.CreateDirectory(extDir);

            // 1. Vorschau-Steuerelement (Phase 11) und DPI-/Auflösungsunabhängigkeit (Kapitel 18.2 & 18.3)
            using (var preview = new DesktopPreviewControl())
            using (var bgBmp = new Bitmap(1920, 1080, PixelFormat.Format32bppArgb))
            using (var stBmp = new Bitmap(400, 200, PixelFormat.Format32bppArgb))
            {
                preview.Size = new Size(640, 360);
                preview.SetDesktopBackground(bgBmp, false, Color.Black, WallpaperPositionMode.Fill, 1920, 1080);
                preview.SetStickerImage(stBmp);
                preview.UpdateStickerParameters(0.25, 0.40, 0.30, 20.0);

                Assert(Math.Abs(preview.RelativeWidth - 0.25) < 0.001, "Vorschau-Breite muss exakt übernommen werden.");
                Assert(Math.Abs(preview.RelativeX - 0.40) < 0.001, "Vorschau-X muss exakt übernommen werden.");
                Assert(Math.Abs(preview.RelativeY - 0.30) < 0.001, "Vorschau-Y muss exakt übernommen werden.");

                // Auflösungswechsel auf 3440x1440 simulieren: relative Werte bleiben erhalten und werden neu berechnet
                StickerPlacementResult res3440 = StickerGeometry.CalculatePlacement(400, 200, 3440, 1440, preview.RelativeWidth, preview.RelativeX, preview.RelativeY);
                Assert(res3440.Width == 860 && res3440.Height == 430, "Nach Auflösungswechsel auf 3440x1440 müssen Pixelmaße proportional (860x430) neu berechnet werden.");
            }

            // 2. Bearbeiten & Ersetzen: Alte Stickerkopie wird erst NACH erfolgreichem Speichern gelöscht (Phase 12 / Kapitel 14)
            string origBgFile = Path.Combine(extDir, "orig.png");
            CreateTestBitmap(origBgFile, 1280, 720, Color.DarkSlateBlue);
            mockService.CurrentState = mockService.CurrentState with
            {
                WallpaperFilePath = origBgFile,
                ScreenWidth = 1280,
                ScreenHeight = 720,
                PositionMode = WallpaperPositionMode.Fill
            };

            var settings = new AppSettings();
            backupMgr.CreateOrUpdateOriginalBackup(mockService.GetCurrentState(), settings);

            string stickerFile1 = Path.Combine(extDir, "s1.png");
            string stickerFile2 = Path.Combine(extDir, "s2.png");
            CreateTestBitmap(stickerFile1, 200, 200, Color.Orange);
            CreateTestBitmap(stickerFile2, 300, 150, Color.Cyan);

            using LoadedStickerImage s1 = StickerImageLoader.ImportFromUserFile(stickerFile1, paths);
            using Bitmap origBmp = StickerImageLoader.LoadAndNormalizeBitmap(settings.OriginalBackupFilePath);
            settings.StickerFilePath = s1.LocalFilePath;
            string s1LocalPath = s1.LocalFilePath;

            ActivationResult act1 = activator.ComposeAndActivate(mockService.GetCurrentState(), settings, origBmp, s1.Bitmap);
            Assert(act1.Status == ActivationStatus.Success, "Erstes Sticker-Anwenden muss gelingen.");
            Assert(File.Exists(s1LocalPath), "Aktive Stickerkopie 1 muss existieren.");

            // Zweites Bild importieren: Solange noch nicht gespeichert wurde, existiert Stickerkopie 1 weiterhin!
            using LoadedStickerImage s2 = StickerImageLoader.ImportFromUserFile(stickerFile2, paths);
            Assert(File.Exists(s1LocalPath) && File.Exists(s2.LocalFilePath),
                "Vor dem erfolgreichen Speichern müssen sowohl die alte als auch die neue Stickerkopie vorhanden sein.");

            // Jetzt mit neuem Bild speichern -> alte Kopie 1 wird automatisch bereinigt, neue Kopie 2 bleibt!
            s1.Dispose();
            settings.StickerFilePath = s2.LocalFilePath;
            ActivationResult act2 = activator.ComposeAndActivate(mockService.GetCurrentState(), settings, origBmp, s2.Bitmap);
            Assert(act2.Status == ActivationStatus.Success, "Zweites Sticker-Anwenden muss gelingen.");
            Assert(!File.Exists(s1LocalPath), "Alte Stickerkopie 1 muss nach erfolgreichem Ersetzen bereinigt werden.");
            Assert(File.Exists(s2.LocalFilePath), "Neue Stickerkopie 2 muss erhalten bleiben.");

            // 3. Fehlende Originaldatei beim Deaktivieren (Phase 13 / Kapitel 15 & 21)
            string backupPathTemp = settings.OriginalBackupFilePath;
            settings.OriginalBackupFilePath = Path.Combine(paths.OriginalsDir, "nicht_vorhanden.png");
            string activeWallpaperBeforeFailedRestore = mockService.GetCurrentState().WallpaperFilePath;
            RestoreResult failedRestore = backupMgr.RestoreOriginalWallpaper(settings, mockService, settingsMgr);
            Assert(failedRestore.Status == RestoreResultStatus.MissingOrInvalidBackup, "Fehlende Originaldatei muss beim Deaktivieren erkannt werden.");
            Assert(mockService.GetCurrentState().WallpaperFilePath == activeWallpaperBeforeFailedRestore,
                "Fehlende Originaldatei darf den bestehenden Desktop nicht überschreiben.");
            settings.OriginalBackupFilePath = backupPathTemp;

            // 4. Desktop-Kontextmenü-Integration inkl. verschobener EXE (Phase 14 / Kapitel 16)
            string fakeExe1 = Path.Combine(extDir, "DesktopSticker_v1.exe");
            string fakeExe2 = Path.Combine(extDir, "MovedFolder", "DesktopSticker_v1.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(fakeExe2)!);
            File.WriteAllBytes(fakeExe1, new byte[] { 0x4D, 0x5A });
            File.WriteAllBytes(fakeExe2, new byte[] { 0x4D, 0x5A });

            var memRegStore = new InMemoryRegistryStore();
            var registrar = new ContextMenuRegistrar(testRegKey, memRegStore);
            Assert(!registrar.IsRegistered(), "Test-Kontextmenü darf anfangs nicht registriert sein.");
            registrar.Register(fakeExe1);
            Assert(registrar.IsRegistered(), "Kontextmenü muss nach Register() als registriert erkannt werden.");

            // Verschieben der EXE simulieren -> EnsureUpdatedIfRegistered aktualisiert den Pfad
            registrar.EnsureUpdatedIfRegistered(shouldBeEnabled: true, executablePath: fakeExe2);
            string? cmdVal = memRegStore.GetStringValue($@"{testRegKey}\{ContextMenuRegistrar.EditVerbKeyName}\command", null);
            Assert(cmdVal is not null && cmdVal.Contains(fakeExe2, StringComparison.OrdinalIgnoreCase),
                "Kontextmenü muss nach Verschieben der ausführbaren Datei auf den neuen Pfad aktualisiert werden.");

            registrar.Unregister();
            Assert(!registrar.IsRegistered(), "Kontextmenü muss nach Unregister() entfernt sein.");

            // 5. Mehrfaches Starten verhindern (Kapitel 18.6 & 21)
            string testMutexName = $@"Local\DesktopSticker_TestMutex_{Guid.NewGuid():N}";
            using (var instance1 = new SingleInstanceGuard(testMutexName))
            {
                Assert(instance1.IsFirstInstance, "Erste Instanz muss Mutex erhalten.");
                bool secondIsFirst = Task.Run(() =>
                {
                    using var instance2 = new SingleInstanceGuard(testMutexName);
                    return instance2.IsFirstInstance;
                }).GetAwaiter().GetResult();
                Assert(!secondIsFirst, "Zweite gleichzeitige Instanz muss erkannt und blockiert werden.");
            }

            // 5b. Autostart-Registrierung für das Desktop-Overlay (Wallpaper-Engine-Kompatibilität)
            var autostartRegistrar = new AutostartRegistrar(@"Software\DesktopSticker_TestRun", memRegStore);
            Assert(!autostartRegistrar.IsRegistered(), "Autostart darf anfangs nicht registriert sein.");
            autostartRegistrar.Register(fakeExe1);
            Assert(autostartRegistrar.IsRegistered(), "Autostart muss nach Register() aktiv sein.");
            string? runCmd = memRegStore.GetStringValue(@"Software\DesktopSticker_TestRun", AutostartRegistrar.ValueName);
            Assert(runCmd is not null && runCmd.Contains("--overlay", StringComparison.OrdinalIgnoreCase), "Autostart muss mit --overlay Parameter registriert werden.");

            // 5c. Multi-Sticker-Stapel, Migration und Styleguide-Controls prüfen
            var multiSettings = new AppSettings
            {
                StickerFilePath = s2.LocalFilePath,
                RelativeWidth = 0.15,
                RelativeX = 0.25,
                RelativeY = 0.30,
                CornerRadiusPercent = 18.0
            };
            multiSettings.SyncLegacyAndMultiStickerFields();
            Assert(multiSettings.Stickers.Count == 1, "Legacy-Einzelsticker muss automatisch in die Stickers-Liste migriert werden.");

            var secondItem = new StickerItemConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = "Bild 2",
                StickerFilePath = s2.LocalFilePath,
                RelativeWidth = 0.22,
                RelativeX = 0.65,
                RelativeY = 0.55,
                CornerRadiusPercent = 35.0
            };
            multiSettings.Stickers.Add(secondItem);
            multiSettings.SelectedStickerId = secondItem.Id;
            multiSettings.SyncLegacyAndMultiStickerFields();
            settingsMgr.Save(multiSettings);

            SettingsLoadResult reloadedMulti = settingsMgr.Load();
            Assert(reloadedMulti.Status == SettingsLoadStatus.LoadedSuccessfully, "Multi-Sticker-Konfiguration muss erfolgreich geladen werden.");
            Assert(reloadedMulti.Settings.Stickers.Count == 2, "Geladene Konfiguration muss beide Bilder im Stapel enthalten.");
            Assert(reloadedMulti.Settings.SelectedStickerId == secondItem.Id, "Ausgewählte Sticker-ID muss erhalten bleiben.");

            using (var vTab = new StickerVerticalTabControl())
            using (var pillBtn = new TactilePillButton { Text = "Anwenden", ButtonStyle = TactileButtonStyle.PrimaryBlue })
            using (var slider = new TactileSlider { Minimum = 0, Maximum = 100, Value = 42 })
            using (var chip = new TactileCheckChip { Text = "Option", Checked = true })
            {
                Assert(slider.Value == 42, "TactileSlider muss Wert korrekt halten.");
                vTab.SetEntries(
                    reloadedMulti.Settings.Stickers.Select(cfg => new StickerTabEntry { Config = cfg, Bitmap = null }),
                    secondItem.Id);
                Assert(vTab.SelectedStickerId == secondItem.Id, "StickerVerticalTabControl muss ausgewählten Sticker übernehmen.");
            }

            // Ursprüngliche Test-Settings für den anschließenden Deinstallationstest wieder speichern
            settingsMgr.Save(settings);

            // 6a. Recovery bei gelöschter settings.json (FirstLaunch) prüfen:
            // Selbst wenn settings.json gelöscht wurde, muss UninstallCoordinator die Sicherung im Originals-Ordner finden
            // und das aktive generierte Wallpaper zuverlässig durch das Original ersetzen!
            File.Delete(paths.SettingsFilePath);
            SettingsLoadResult recoveredFirstLaunch = settingsMgr.Load();
            Assert(recoveredFirstLaunch.Status == SettingsLoadStatus.FirstLaunch, "Gelöschte settings.json muss FirstLaunch melden.");
            Assert(recoveredFirstLaunch.Settings.HasOriginalBackup, "Bei gelöschter settings.json muss das vorhandene Backup im Originals-Ordner automatisch erkannt werden.");

            // 6b. Blockierte Registry bei Kontextmenü-Deinstallation darf kein falsch-positives ContextMenuRemoved=true liefern
            registrar.Register(fakeExe1);
            memRegStore.ThrowOnDelete = true;
            memRegStore.ThrowOnRead = true;
            Assert(!registrar.Unregister(), "Bei blockiertem Registry-Zugriff muss Unregister() false zurückgeben.");
            memRegStore.ThrowOnDelete = false;
            memRegStore.ThrowOnRead = false;

            // 6c. Geordnete Deinstallation (Kapitel 22) – auch nach gelöschter settings.json!
            var uninstallCoord = new UninstallCoordinator(paths, mockService, backupMgr, settingsMgr, registrar, autostartRegistrar, stopOverlayProcess: false);
            s2.Dispose();
            UninstallPrepareResult uninstRes = uninstallCoord.Execute(userRequestedDeleteUserData: true);
            Assert(uninstRes.OriginalRestoredOrNotNeeded, "Deinstallation muss zuerst das Original-Wallpaper wiederherstellen (auch wenn settings.json fehlte).");
            Assert(uninstRes.ContextMenuRemoved, "Deinstallation muss Kontextmenüeinträge entfernen.");
            Assert(!autostartRegistrar.IsRegistered(), "Deinstallation muss den Autostart-Eintrag entfernen.");
            Assert(uninstRes.UserDataDeleted, "Deinstallation muss nach Wiederherstellung auf Wunsch die Benutzerdaten löschen.");
            Assert(!Directory.Exists(paths.RootDir), "Anwendungsdatenordner muss nach Deinstallation gelöscht sein.");

            Console.WriteLine("[Phase 11-16] Erfolgreich geprüft.");
        }
        finally
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\{testRegKey.Split('\\')[1]}", throwOnMissingSubKey: false);
            }
            catch
            {
                // Ignorieren
            }

            try
            {
                if (Directory.Exists(testDir))
                {
                    Directory.Delete(testDir, recursive: true);
                }
            }
            catch
            {
                // Ignorieren
            }
        }
    }

    private static void RunGifTests()
    {
        Console.WriteLine("[GIF-Tests] Teste statische und animierte GIF-Unterstützung...");

        string testDir = Path.Combine(Path.GetTempPath(), $"DesktopSticker_TestGif_{Guid.NewGuid():N}");
        try
        {
            var paths = new AppStoragePaths(testDir);
            paths.EnsureDirectoriesAndWriteAccess();

            string userDir = Path.Combine(testDir, "UserGifs");
            Directory.CreateDirectory(userDir);

            // 1. Statisches GIF testen
            string staticGifPath = Path.Combine(userDir, "static_sample.gif");
            using (var bmp = new Bitmap(60, 40, PixelFormat.Format32bppArgb))
            {
                using Graphics g = Graphics.FromImage(bmp);
                g.Clear(Color.DeepSkyBlue);
                bmp.Save(staticGifPath, ImageFormat.Gif);
            }

            using LoadedStickerImage loadedStaticGif = StickerImageLoader.ImportFromUserFile(staticGifPath, paths);
            Assert(loadedStaticGif.IsGif, "Statisches GIF muss als IsGif erkannt werden.");
            Assert(!loadedStaticGif.IsAnimatedGif, "Statisches 1-Frame-GIF darf nicht als animiert gelten.");
            Assert(loadedStaticGif.FrameCount == 1, "Statisches GIF muss FrameCount == 1 haben.");
            Assert(Path.GetExtension(loadedStaticGif.LocalFilePath).Equals(".gif", StringComparison.OrdinalIgnoreCase), "Lokale Kopie eines GIFs muss als .gif gespeichert werden.");

            // 2. Echtes animiertes 2-Frame-GIF (GIF89a: Frame 0 = Rot @ 120ms, Frame 1 = Grün @ 150ms) testen
            string animGifPath = Path.Combine(userDir, "animated_sample.gif");
            byte[] gif89aTwoFrames = new byte[]
            {
                // Header GIF89a
                0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
                // Logical Screen Descriptor: 1x1, GCT (2 Farben)
                0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
                // Global Color Table: Farbe 0 = Rot (FF,00,00), Farbe 1 = Grün (00,FF,00)
                0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00,
                // Netscape 2.0 Loop Extension
                0x21, 0xFF, 0x0B, 0x4E, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2E, 0x30, 0x03, 0x01, 0x00, 0x00, 0x00,
                // Frame 0 Graphic Control Extension (12 * 10ms = 120ms)
                0x21, 0xF9, 0x04, 0x00, 0x0C, 0x00, 0x00, 0x00,
                // Frame 0 Image Descriptor (0,0 1x1)
                0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
                // Frame 0 Image Data (Farbindex 0 = Rot)
                0x02, 0x02, 0x44, 0x01, 0x00,
                // Frame 1 Graphic Control Extension (15 * 10ms = 150ms)
                0x21, 0xF9, 0x04, 0x00, 0x0F, 0x00, 0x00, 0x00,
                // Frame 1 Image Descriptor (0,0 1x1)
                0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
                // Frame 1 Image Data (Farbindex 1 = Grün)
                0x02, 0x02, 0x4C, 0x01, 0x00,
                // Trailer
                0x3B
            };
            File.WriteAllBytes(animGifPath, gif89aTwoFrames);

            using LoadedStickerImage loadedAnimGif = StickerImageLoader.ImportFromUserFile(animGifPath, paths);
            Assert(loadedAnimGif.IsGif, "Animiertes GIF muss als IsGif erkannt werden.");
            Assert(loadedAnimGif.IsAnimatedGif, "Animiertes 2-Frame-GIF muss als IsAnimatedGif erkannt werden.");
            Assert(loadedAnimGif.FrameCount == 2, $"Animiertes GIF muss 2 Frames haben (erhalten: {loadedAnimGif.FrameCount}).");
            Assert(loadedAnimGif.FrameDelaysMs.Count == 2, "FrameDelaysMs muss 2 Einträge besitzen.");
            Assert(loadedAnimGif.FrameDelaysMs[0] == 120, $"Frame 0 Delay muss 120ms sein (erhalten: {loadedAnimGif.FrameDelaysMs[0]}).");
            Assert(loadedAnimGif.FrameDelaysMs[1] == 150, $"Frame 1 Delay muss 150ms sein (erhalten: {loadedAnimGif.FrameDelaysMs[1]}).");

            Color f0Col = loadedAnimGif.Frames[0].GetPixel(0, 0);
            Color f1Col = loadedAnimGif.Frames[1].GetPixel(0, 0);
            Assert(f0Col.R == 255 && f0Col.G == 0, $"Frame 0 muss Rot sein (erhalten: R={f0Col.R}, G={f0Col.G}).");
            Assert(f1Col.R == 0 && f1Col.G == 255, $"Frame 1 muss Grün sein (erhalten: R={f1Col.R}, G={f1Col.G}).");

            // 3. Erneutes Laden aus der lokalen Kopie prüfen (inkl. Erhalt aller Frames)
            using LoadedStickerImage reloadedAnimGif = StickerImageLoader.LoadFromLocalCopy(loadedAnimGif.LocalFilePath);
            Assert(reloadedAnimGif.IsAnimatedGif && reloadedAnimGif.FrameCount == 2, "Geladene lokale GIF-Kopie muss weiterhin animiert mit 2 Frames sein.");

            Console.WriteLine("[GIF-Tests] Erfolgreich geprüft.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(testDir))
                {
                    Directory.Delete(testDir, recursive: true);
                }
            }
            catch
            {
                // Ignorieren
            }
        }
    }

    private static void CreateTestBitmap(string path, int width, int height, Color color)
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(color);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion fehlgeschlagen: {message}");
        }
    }
}
