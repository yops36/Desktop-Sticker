using System.Globalization;
using System.Reflection;
using System.Text.Json;
using DesktopSticker.Configuration;

namespace DesktopSticker.Localization;

/// <summary>
/// Beschreibt eine auswählbare Oberflächensprache.
/// </summary>
public sealed record UiLanguageInfo(string Code, string NativeName, string EnglishName);

/// <summary>
/// Zentraler, schlanker Übersetzungsdienst für alle UI-Texte.
/// Die Wörterbücher liegen als eingebettete JSON-Ressourcen unter Localization/Languages/{code}.json
/// (funktioniert dadurch auch im Single-File-Publish ohne zusätzliche Dateien).
/// Fallback-Reihenfolge: gewählte Sprache → Englisch → Schlüssel selbst.
/// </summary>
public static class Localizer
{
    public const string DefaultLanguageCode = "en";

    private const string ResourcePrefix = "DesktopSticker.Localization.Languages.";
    private const string DefaultStickerNamePrefixKey = "Sticker.DefaultNamePrefix";

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> _current = new Dictionary<string, string>();
    private static IReadOnlyDictionary<string, string> _fallback = new Dictionary<string, string>();
    private static bool _initialized;

    public static IReadOnlyList<UiLanguageInfo> SupportedLanguages { get; } = new[]
    {
        new UiLanguageInfo("en", "English", "English"),
        new UiLanguageInfo("de", "Deutsch", "German"),
        new UiLanguageInfo("es", "Español", "Spanish"),
        new UiLanguageInfo("fr", "Français", "French"),
        new UiLanguageInfo("zh", "中文（简体）", "Chinese (Simplified)"),
        new UiLanguageInfo("hr", "Hrvatski", "Croatian")
    };

    public static string CurrentLanguageCode { get; private set; } = DefaultLanguageCode;

    /// <summary>
    /// Wandelt beliebige Kulturkennungen (z. B. „de-AT“, „zh-Hans-CN“) in einen unterstützten Sprachcode um.
    /// Unbekannte oder leere Werte ergeben die Standard-Sprache Englisch.
    /// </summary>
    public static string NormalizeLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return DefaultLanguageCode;
        }

        string twoLetter = code.Trim().Split('-', '_')[0].ToLowerInvariant();
        return SupportedLanguages.Any(l => l.Code == twoLetter) ? twoLetter : DefaultLanguageCode;
    }

    public static bool IsSupportedLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string twoLetter = code.Trim().Split('-', '_')[0].ToLowerInvariant();
        return SupportedLanguages.Any(l => l.Code == twoLetter);
    }

    /// <summary>
    /// Liefert die Windows-Oberflächensprache, sofern sie unterstützt wird (nur als Vorauswahl im Sprachdialog).
    /// </summary>
    public static string DetectSystemLanguageCode()
    {
        try
        {
            return NormalizeLanguageCode(CultureInfo.InstalledUICulture.Name);
        }
        catch
        {
            return DefaultLanguageCode;
        }
    }

    /// <summary>
    /// Aktiviert die in den AppSettings gespeicherte Sprache (ohne Benutzerinteraktion).
    /// </summary>
    public static void InitializeFromSettings(SettingsManager settingsManager)
    {
        string? stored = null;
        try
        {
            stored = settingsManager.Load().Settings.UiLanguage;
        }
        catch
        {
            // Ignorieren – Standard-Sprache verwenden
        }

        SetLanguage(stored);
    }

    public static void SetLanguage(string? code)
    {
        string normalized = NormalizeLanguageCode(code);
        lock (SyncRoot)
        {
            _fallback = LoadDictionary(DefaultLanguageCode);
            _current = LoadDictionary(normalized);
            CurrentLanguageCode = normalized;
            _initialized = true;
        }
    }

    /// <summary>Übersetzt einen Schlüssel in die aktuelle Oberflächensprache.</summary>
    public static string T(string key)
    {
        EnsureInitialized();

        if (_current.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (_fallback.TryGetValue(key, out string? fallbackValue) && !string.IsNullOrEmpty(fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    /// <summary>Übersetzt einen Schlüssel und setzt Platzhalter ({0}, {1:0.0}, …) ein.</summary>
    public static string T(string key, params object?[] args)
    {
        string pattern = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, pattern, args);
        }
        catch (FormatException)
        {
            return pattern;
        }
    }

    /// <summary>
    /// Übersetzt einen Schlüssel für eine bestimmte Sprache, ohne die aktive Sprache zu wechseln
    /// (wird für die Live-Vorschau im Sprachauswahlfenster verwendet).
    /// </summary>
    public static string TFor(string languageCode, string key)
    {
        IReadOnlyDictionary<string, string> dict = LoadDictionary(NormalizeLanguageCode(languageCode));
        if (dict.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        IReadOnlyDictionary<string, string> fallback = LoadDictionary(DefaultLanguageCode);
        return fallback.TryGetValue(key, out string? fb) && !string.IsNullOrEmpty(fb) ? fb : key;
    }

    /// <summary>
    /// Prüft, ob ein Anzeigename ein automatisch vergebener Standardname ist („Image 1“, „Bild 1“, „Slika 1“ …) –
    /// sprachübergreifend, damit bereits gespeicherte Namen nach einem Sprachwechsel weiterhin erkannt werden.
    /// </summary>
    public static bool IsDefaultStickerName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        foreach (UiLanguageInfo lang in SupportedLanguages)
        {
            IReadOnlyDictionary<string, string> dict = LoadDictionary(lang.Code);
            if (dict.TryGetValue(DefaultStickerNamePrefixKey, out string? prefix) &&
                !string.IsNullOrEmpty(prefix) &&
                displayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureInitialized()
    {
        if (!_initialized)
        {
            SetLanguage(DefaultLanguageCode);
        }
    }

    private static IReadOnlyDictionary<string, string> LoadDictionary(string code)
    {
        lock (SyncRoot)
        {
            if (Cache.TryGetValue(code, out IReadOnlyDictionary<string, string>? cached))
            {
                return cached;
            }

            IReadOnlyDictionary<string, string> loaded = new Dictionary<string, string>();
            try
            {
                Assembly asm = typeof(Localizer).Assembly;
                using Stream? stream = asm.GetManifestResourceStream($"{ResourcePrefix}{code}.json");
                if (stream is not null)
                {
                    loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                             ?? new Dictionary<string, string>();
                }
            }
            catch
            {
                // Beschädigte Sprachdatei: auf Englisch bzw. Schlüssel zurückfallen
            }

            Cache[code] = loaded;
            return loaded;
        }
    }
}
