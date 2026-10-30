using System.Text;
using System.Text.Json;
using DesktopSticker.Localization;

namespace DesktopSticker.Configuration;

public enum SettingsLoadStatus
{
    FirstLaunch,
    LoadedSuccessfully,
    CorruptedOrIncomplete
}

public sealed record SettingsLoadResult(
    SettingsLoadStatus Status,
    AppSettings Settings,
    string? ErrorMessage = null);

/// <summary>
/// Verwaltet das Laden, Validieren und ausfallsichere Speichern der Benutzereinstellungen im JSON-Format (Kapitel 12, 18.5, 18.7).
/// </summary>
public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppStoragePaths _paths;

    public SettingsManager(AppStoragePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_paths.SettingsFilePath))
        {
            return new SettingsLoadResult(SettingsLoadStatus.FirstLaunch, CreateRecoveredSettings());
        }

        try
        {
            string json = File.ReadAllText(_paths.SettingsFilePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SettingsLoadResult(
                    SettingsLoadStatus.CorruptedOrIncomplete,
                    CreateRecoveredSettings(),
                    Localizer.T("Settings.Error.Empty"));
            }

            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is null || loaded.FormatVersion <= 0)
            {
                return new SettingsLoadResult(
                    SettingsLoadStatus.CorruptedOrIncomplete,
                    CreateRecoveredSettings(),
                    Localizer.T("Settings.Error.InvalidFormat"));
            }

            // Werte auf gültige Bereiche begrenzen
            if (double.IsNaN(loaded.RelativeWidth) || double.IsInfinity(loaded.RelativeWidth) || loaded.RelativeWidth <= 0)
            {
                loaded.RelativeWidth = AppSettings.DefaultRelativeWidth;
            }

            if (double.IsNaN(loaded.RelativeX) || double.IsInfinity(loaded.RelativeX))
            {
                loaded.RelativeX = AppSettings.DefaultRelativeX;
            }

            if (double.IsNaN(loaded.RelativeY) || double.IsInfinity(loaded.RelativeY))
            {
                loaded.RelativeY = AppSettings.DefaultRelativeY;
            }

            if (double.IsNaN(loaded.CornerRadiusPercent) || double.IsInfinity(loaded.CornerRadiusPercent))
            {
                loaded.CornerRadiusPercent = AppSettings.DefaultCornerRadiusPercent;
            }

            loaded.CornerRadiusPercent = Math.Clamp(loaded.CornerRadiusPercent, 0.0, 100.0);
            loaded.Stickers ??= new List<StickerItemConfig>();

            for (int i = 0; i < loaded.Stickers.Count; i++)
            {
                StickerItemConfig item = loaded.Stickers[i];
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    item.Id = Guid.NewGuid().ToString("N");
                }

                if (string.IsNullOrWhiteSpace(item.DisplayName))
                {
                    item.DisplayName = Localizer.T("Sticker.DefaultName", i + 1);
                }

                if (double.IsNaN(item.RelativeWidth) || double.IsInfinity(item.RelativeWidth) || item.RelativeWidth <= 0)
                {
                    item.RelativeWidth = AppSettings.DefaultRelativeWidth;
                }

                if (double.IsNaN(item.RelativeX) || double.IsInfinity(item.RelativeX))
                {
                    item.RelativeX = AppSettings.DefaultRelativeX;
                }

                if (double.IsNaN(item.RelativeY) || double.IsInfinity(item.RelativeY))
                {
                    item.RelativeY = AppSettings.DefaultRelativeY;
                }

                if (double.IsNaN(item.CornerRadiusPercent) || double.IsInfinity(item.CornerRadiusPercent))
                {
                    item.CornerRadiusPercent = AppSettings.DefaultCornerRadiusPercent;
                }

                item.CornerRadiusPercent = Math.Clamp(item.CornerRadiusPercent, 0.0, 100.0);
            }

            loaded.SyncLegacyAndMultiStickerFields();

            // Falls ein Original-Backup laut Einstellung vorhanden sein soll, prüfen ob die Angabe konsistent ist
            if (loaded.HasOriginalBackup && !loaded.OriginalWasSolidColorOnly && string.IsNullOrWhiteSpace(loaded.OriginalBackupFilePath))
            {
                return new SettingsLoadResult(
                    SettingsLoadStatus.CorruptedOrIncomplete,
                    CreateRecoveredSettings(),
                    Localizer.T("Settings.Error.MissingBackupPath"));
            }

            return new SettingsLoadResult(SettingsLoadStatus.LoadedSuccessfully, loaded);
        }
        catch (Exception ex)
        {
            return new SettingsLoadResult(
                SettingsLoadStatus.CorruptedOrIncomplete,
                CreateRecoveredSettings(),
                Localizer.T("Settings.Error.ReadFailed", ex.Message));
        }
    }

    /// <summary>
    /// Speichert die Einstellungen atomar über eine temporäre Datei, damit bei einem Absturz oder Schreibfehler
    /// niemals eine halb geschriebene Konfigurationsdatei zurückbleibt (Kapitel 18.5 & 18.7).
    /// </summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _paths.EnsureDirectoriesAndWriteAccess();

        string tempFile = Path.Combine(_paths.TempDir, $"settings_{Guid.NewGuid():N}.json.tmp");
        try
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }

            File.Move(tempFile, _paths.SettingsFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
                // Ignorieren
            }

            throw new IOException($"Die Einstellungen konnten nicht gespeichert werden: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Sucht im Originals-Ordner nach einer eventuell bereits vorhandenen Sicherung, falls settings.json beschädigt wurde.
    /// </summary>
    private AppSettings CreateRecoveredSettings()
    {
        var recovered = new AppSettings();
        try
        {
            if (Directory.Exists(_paths.OriginalsDir))
            {
                string? existingBackup = Directory.GetFiles(_paths.OriginalsDir)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(existingBackup) && File.Exists(existingBackup))
                {
                    recovered.HasOriginalBackup = true;
                    recovered.OriginalBackupFilePath = existingBackup;
                    recovered.OriginalBackupFileHash = AppStoragePaths.ComputeFileSha256(existingBackup);
                }
            }
        }
        catch
        {
            // Ignorieren
        }

        return recovered;
    }
}
