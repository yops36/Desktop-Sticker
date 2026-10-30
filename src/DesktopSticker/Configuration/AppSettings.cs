using System.Drawing;
using System.Text.Json.Serialization;
using DesktopSticker.Localization;
using DesktopSticker.Wallpaper;

namespace DesktopSticker.Configuration;

/// <summary>
/// Konfiguration eines einzelnen Sticker-Bildes in der vertikalen Bilderliste.
/// </summary>
public sealed class StickerItemConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string StickerFilePath { get; set; } = string.Empty;

    public double RelativeWidth { get; set; } = AppSettings.DefaultRelativeWidth;

    public double RelativeX { get; set; } = AppSettings.DefaultRelativeX;

    public double RelativeY { get; set; } = AppSettings.DefaultRelativeY;

    public double CornerRadiusPercent { get; set; } = AppSettings.DefaultCornerRadiusPercent;

    public StickerItemConfig Clone()
    {
        return (StickerItemConfig)MemberwiseClone();
    }
}

/// <summary>
/// Dauerhaft gespeicherte Benutzerkonfiguration und Sicherungsmetadaten (Kapitel 5 & 12).
/// Unterstützt sowohl ein einzelnes Bild (Abwärtskompatibilität) als auch mehrere Bilder (Multi-Sticker-Liste).
/// </summary>
public sealed class AppSettings
{
    public const int CurrentFormatVersion = 2;

    public const double DefaultRelativeWidth = 0.20;
    public const double DefaultRelativeX = 0.40;
    public const double DefaultRelativeY = 0.35;
    public const double DefaultCornerRadiusPercent = 0.0;

    /// <summary>Versionsnummer des Einstellungsformats.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Monitor-Kennung bei der Einrichtung.</summary>
    public string MonitorId { get; set; } = string.Empty;

    /// <summary>Gibt an, ob überhaupt schon eine Originalsicherung angelegt wurde.</summary>
    public bool HasOriginalBackup { get; set; }

    /// <summary>Pfad zur gesicherten lokalen Kopie des Originalhintergrundbildes.</summary>
    public string OriginalBackupFilePath { get; set; } = string.Empty;

    /// <summary>Ursprünglicher Dateipfad des Hintergrundbildes vor der Änderung durch Desktop Sticker.</summary>
    public string OriginalSourceFilePath { get; set; } = string.Empty;

    /// <summary>SHA-256-Hash der gesicherten Originaldatei.</summary>
    public string OriginalBackupFileHash { get; set; } = string.Empty;

    /// <summary>Rot-Anteil der ursprünglichen Hintergrundfarbe (0-255).</summary>
    public int OriginalBackgroundColorR { get; set; }

    /// <summary>Grün-Anteil der ursprünglichen Hintergrundfarbe (0-255).</summary>
    public int OriginalBackgroundColorG { get; set; }

    /// <summary>Blau-Anteil der ursprünglichen Hintergrundfarbe (0-255).</summary>
    public int OriginalBackgroundColorB { get; set; }

    /// <summary>Ursprünglicher Wallpaper-Anzeigemodus.</summary>
    public WallpaperPositionMode OriginalPositionMode { get; set; } = WallpaperPositionMode.Fill;

    /// <summary>Gibt an, ob ursprünglich nur eine Hintergrundfarbe ohne Bild verwendet wurde.</summary>
    public bool OriginalWasSolidColorOnly { get; set; }

    /// <summary>Bildschirmbreite in Pixeln bei der ursprünglichen Sicherung.</summary>
    public int OriginalScreenWidth { get; set; }

    /// <summary>Bildschirmhöhe in Pixeln bei der ursprünglichen Sicherung.</summary>
    public int OriginalScreenHeight { get; set; }

    /// <summary>Pfad zur lokal gespeicherten Kopie des ausgewählten Stickerbildes (Abwärtskompatibilität / Primärsticker).</summary>
    public string StickerFilePath { get; set; } = string.Empty;

    /// <summary>Stickerbreite als relativer Anteil der Monitorbreite (z. B. 0.20 = 20 %).</summary>
    public double RelativeWidth { get; set; } = DefaultRelativeWidth;

    /// <summary>Horizontale Position als relativer Anteil der Monitorbreite vom linken Rand (0.0 bis 1.0).</summary>
    public double RelativeX { get; set; } = DefaultRelativeX;

    /// <summary>Vertikale Position als relativer Anteil der Monitorhöhe vom oberen Rand (0.0 bis 1.0).</summary>
    public double RelativeY { get; set; } = DefaultRelativeY;

    /// <summary>Eckenradius in Prozent (0 = rechteckig, 100 = maximal sinnvolle Abrundung).</summary>
    public double CornerRadiusPercent { get; set; } = DefaultCornerRadiusPercent;

    /// <summary>Liste aller hinzugefügten Sticker-Bilder (in Stapelreihenfolge).</summary>
    public List<StickerItemConfig> Stickers { get; set; } = new();

    /// <summary>ID des aktuell im Editor ausgewählten Stickers.</summary>
    public string SelectedStickerId { get; set; } = string.Empty;

    /// <summary>Pfad zum aktuell generierten Wallpaper im Generated-Ordner.</summary>
    public string CurrentGeneratedWallpaperPath { get; set; } = string.Empty;

    /// <summary>SHA-256-Hash des aktuell generierten Wallpapers zur eindeutigen Wiedererkennung.</summary>
    public string CurrentGeneratedWallpaperHash { get; set; } = string.Empty;

    /// <summary>Gibt an, ob das Sticker-Wallpaper bzw. Overlay derzeit aktiv geschaltet ist.</summary>
    public bool IsStickerActive { get; set; }

    /// <summary>Bildschirmbreite in Pixeln bei der letzten Wallpaper-Generierung.</summary>
    public int LastGeneratedScreenWidth { get; set; }

    /// <summary>Bildschirmhöhe in Pixeln bei der letzten Wallpaper-Generierung.</summary>
    public int LastGeneratedScreenHeight { get; set; }

    /// <summary>Gibt an, ob die optionale Desktop-Kontextmenüintegration aktiviert ist.</summary>
    public bool ContextMenuEnabled { get; set; }

    /// <summary>Gewählte Oberflächensprache (ISO-Code: en, de, es, fr, zh, hr). Leer = noch nicht ausgewählt (Standard: Englisch).</summary>
    public string UiLanguage { get; set; } = string.Empty;

    [JsonIgnore]
    public Color OriginalBackgroundColor => Color.FromArgb(
        255,
        Math.Clamp(OriginalBackgroundColorR, 0, 255),
        Math.Clamp(OriginalBackgroundColorG, 0, 255),
        Math.Clamp(OriginalBackgroundColorB, 0, 255));

    /// <summary>
    /// Synchronisiert die Einzel-Eigenschaften (StickerFilePath, RelativeWidth usw.) mit der Stickers-Liste.
    /// </summary>
    public void SyncLegacyAndMultiStickerFields()
    {
        Stickers ??= new List<StickerItemConfig>();

        // Migration von Format v1 (nur StickerFilePath gesetzt, Liste noch leer)
        if (Stickers.Count == 0 && !string.IsNullOrWhiteSpace(StickerFilePath))
        {
            var migrated = new StickerItemConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = Localizer.T("Sticker.DefaultName", 1),
                StickerFilePath = StickerFilePath,
                RelativeWidth = RelativeWidth,
                RelativeX = RelativeX,
                RelativeY = RelativeY,
                CornerRadiusPercent = CornerRadiusPercent
            };
            Stickers.Add(migrated);
            SelectedStickerId = migrated.Id;
        }

        if (Stickers.Count > 0)
        {
            StickerItemConfig primary = Stickers.FirstOrDefault(s => s.Id == SelectedStickerId) ?? Stickers[0];
            SelectedStickerId = primary.Id;
            StickerFilePath = primary.StickerFilePath;
            RelativeWidth = primary.RelativeWidth;
            RelativeX = primary.RelativeX;
            RelativeY = primary.RelativeY;
            CornerRadiusPercent = primary.CornerRadiusPercent;
        }
        else
        {
            SelectedStickerId = string.Empty;
            StickerFilePath = string.Empty;
        }
    }

    public IReadOnlyList<StickerItemConfig> GetEffectiveStickers()
    {
        if (Stickers is { Count: > 0 })
        {
            return Stickers;
        }

        if (!string.IsNullOrWhiteSpace(StickerFilePath))
        {
            return new[]
            {
                new StickerItemConfig
                {
                    Id = "legacy",
                    DisplayName = Localizer.T("Sticker.DefaultName", 1),
                    StickerFilePath = StickerFilePath,
                    RelativeWidth = RelativeWidth,
                    RelativeX = RelativeX,
                    RelativeY = RelativeY,
                    CornerRadiusPercent = CornerRadiusPercent
                }
            };
        }

        return Array.Empty<StickerItemConfig>();
    }

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.Stickers = Stickers?.Select(s => s.Clone()).ToList() ?? new List<StickerItemConfig>();
        return copy;
    }
}
