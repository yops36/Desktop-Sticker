using DesktopSticker.Configuration;

namespace DesktopSticker.Integration;

/// <summary>
/// Verwaltet den optionalen Autostart-Eintrag unter HKCU\Software\Microsoft\Windows\CurrentVersion\Run,
/// damit das Desktop-Sticker-Overlay nach einer Windows-Anmeldung automatisch und ohne Nutzerinteraktion
/// wieder über Wallpaper Engine / dem Desktop-Hintergrund eingeblendet wird.
/// </summary>
public sealed class AutostartRegistrar
{
    public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "DesktopSticker";

    private readonly string _runKeyPath;
    private readonly IRegistryStore _store;

    public AutostartRegistrar(
        string runKeyPath = DefaultRunKeyPath,
        IRegistryStore? store = null)
    {
        if (string.IsNullOrWhiteSpace(runKeyPath))
        {
            throw new ArgumentException("Der Registrierungspfad darf nicht leer sein.", nameof(runKeyPath));
        }

        _runKeyPath = runKeyPath;
        _store = store ?? new HkcuRegistryStore();
    }

    public bool IsRegistered()
    {
        try
        {
            string? val = _store.GetStringValue(_runKeyPath, ValueName);
            return !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public void Register(string? executablePath = null)
    {
        string? resolved = string.IsNullOrWhiteSpace(executablePath)
            ? Environment.ProcessPath
            : executablePath;

        if (string.IsNullOrWhiteSpace(resolved))
        {
            return;
        }

        string fullPath = Path.GetFullPath(resolved);
        if (!File.Exists(fullPath))
        {
            return;
        }

        try
        {
            _store.SetStringValue(_runKeyPath, ValueName, $"\"{fullPath}\" --overlay");
        }
        catch
        {
            // Ignorieren, falls Registry-Schreibzugriff eingeschränkt ist
        }
    }

    public void Unregister()
    {
        try
        {
            if (_store is HkcuRegistryStore)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                _store.SetStringValue(_runKeyPath, ValueName, string.Empty);
            }
        }
        catch
        {
            // Ignorieren
        }
    }

    public void EnsureSynchronized(AppSettings settings, string? executablePath = null)
    {
        bool hasAnyValidSticker = settings.GetEffectiveStickers()
            .Any(s => !string.IsNullOrWhiteSpace(s.StickerFilePath) && File.Exists(s.StickerFilePath));

        if (settings.IsStickerActive && hasAnyValidSticker)
        {
            Register(executablePath);
        }
        else
        {
            Unregister();
        }
    }
}
