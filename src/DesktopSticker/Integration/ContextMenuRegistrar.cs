using DesktopSticker.Localization;
using Microsoft.Win32;

namespace DesktopSticker.Integration;

/// <summary>
/// Abstraktion für den Benutzer-Registrierungsspeicher (HKCU), um Kontextmenüeinträge
/// auch in isolierten Testumgebungen vollständig prüfen zu können.
/// </summary>
public interface IRegistryStore
{
    void SetStringValue(string subKeyPath, string? valueName, string value);
    string? GetStringValue(string subKeyPath, string? valueName);
    void DeleteSubKeyTree(string subKeyPath);
}

public sealed class HkcuRegistryStore : IRegistryStore
{
    public void SetStringValue(string subKeyPath, string? valueName, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public string? GetStringValue(string subKeyPath, string? valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void DeleteSubKeyTree(string subKeyPath)
    {
        Registry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
    }
}

/// <summary>
/// Verwaltet die optionale Integration in das Windows-Desktop-Kontextmenü innerhalb von HKEY_CURRENT_USER (Kapitel 16).
/// Erfordert keine Administratorrechte.
/// </summary>
public sealed class ContextMenuRegistrar
{
    public const string DefaultShellBaseKeyPath = @"Software\Classes\DesktopBackground\Shell";
    public const string EditVerbKeyName = "DesktopStickerEdit";
    public const string DeactivateVerbKeyName = "DesktopStickerDeactivate";

    public static string EditMenuLabel => Localizer.T("ContextMenu.Edit");
    public static string DeactivateMenuLabel => Localizer.T("ContextMenu.Deactivate");

    private readonly string _shellBaseKeyPath;
    private readonly IRegistryStore _store;

    public ContextMenuRegistrar(
        string shellBaseKeyPath = DefaultShellBaseKeyPath,
        IRegistryStore? store = null)
    {
        if (string.IsNullOrWhiteSpace(shellBaseKeyPath))
        {
            throw new ArgumentException("Der Registrierungspfad darf nicht leer sein.", nameof(shellBaseKeyPath));
        }

        _shellBaseKeyPath = shellBaseKeyPath;
        _store = store ?? new HkcuRegistryStore();
    }

    /// <summary>
    /// Prüft, ob die Kontextmenüeinträge im Benutzerbereich der Windows-Registrierung vorhanden sind.
    /// </summary>
    public bool IsRegistered()
    {
        try
        {
            string? editCmd = _store.GetStringValue($@"{_shellBaseKeyPath}\{EditVerbKeyName}\command", null);
            string? deactCmd = _store.GetStringValue($@"{_shellBaseKeyPath}\{DeactivateVerbKeyName}\command", null);

            return !string.IsNullOrWhiteSpace(editCmd) && !string.IsNullOrWhiteSpace(deactCmd);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Legt die Desktop-Kontextmenüeinträge „Desktop Sticker bearbeiten“ und „Desktop Sticker deaktivieren“
    /// im aktuellen Benutzerkonto (HKCU) an.
    /// </summary>
    public void Register(string? executablePath = null)
    {
        string exePath = ResolveAndValidateExecutablePath(executablePath);

        string editKeyPath = $@"{_shellBaseKeyPath}\{EditVerbKeyName}";
        _store.SetStringValue(editKeyPath, null, EditMenuLabel);
        _store.SetStringValue(editKeyPath, "Icon", $"\"{exePath}\",0");
        _store.SetStringValue($@"{editKeyPath}\command", null, $"\"{exePath}\" --edit");

        string deactKeyPath = $@"{_shellBaseKeyPath}\{DeactivateVerbKeyName}";
        _store.SetStringValue(deactKeyPath, null, DeactivateMenuLabel);
        _store.SetStringValue(deactKeyPath, "Icon", $"\"{exePath}\",0");
        _store.SetStringValue($@"{deactKeyPath}\command", null, $"\"{exePath}\" --deactivate");
    }

    /// <summary>
    /// Entfernt die Desktop-Kontextmenüeinträge vollständig aus der Benutzer-Registrierung (Kapitel 16 & 22).
    /// Gibt true zurück, wenn die Bereinigung fehlerfrei durchgeführt wurde und keine Einträge mehr registriert sind.
    /// </summary>
    public bool Unregister()
    {
        bool deletedWithoutError = true;

        try
        {
            _store.DeleteSubKeyTree($@"{_shellBaseKeyPath}\{EditVerbKeyName}");
        }
        catch
        {
            deletedWithoutError = false;
        }

        try
        {
            _store.DeleteSubKeyTree($@"{_shellBaseKeyPath}\{DeactivateVerbKeyName}");
        }
        catch
        {
            deletedWithoutError = false;
        }

        return deletedWithoutError && !IsRegistered();
    }

    /// <summary>
    /// Aktualisiert den hinterlegten Pfad automatisch, falls die ausführbare Datei verschoben wurde (Kapitel 16).
    /// </summary>
    public void EnsureUpdatedIfRegistered(bool shouldBeEnabled, string? executablePath = null)
    {
        if (!shouldBeEnabled)
        {
            if (IsRegistered())
            {
                Unregister();
            }
            return;
        }

        try
        {
            string exePath = ResolveAndValidateExecutablePath(executablePath);
            string expectedEditCmd = $"\"{exePath}\" --edit";
            string? currentEditCmd = _store.GetStringValue($@"{_shellBaseKeyPath}\{EditVerbKeyName}\command", null);

            if (!string.Equals(currentEditCmd, expectedEditCmd, StringComparison.OrdinalIgnoreCase) || !IsRegistered())
            {
                Register(exePath);
            }
        }
        catch
        {
            // Ignorieren, falls im portablen Testlauf kein gültiger Exe-Pfad vorliegt
        }
    }

    private static string ResolveAndValidateExecutablePath(string? executablePath)
    {
        string? resolved = string.IsNullOrWhiteSpace(executablePath)
            ? Environment.ProcessPath
            : executablePath;

        if (string.IsNullOrWhiteSpace(resolved))
        {
            throw new InvalidOperationException("Der Pfad der ausführbaren Anwendung konnte nicht ermittelt werden.");
        }

        string fullPath = Path.GetFullPath(resolved);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Die ausführbare Anwendungsdatei für das Kontextmenü existiert nicht.", fullPath);
        }

        return fullPath;
    }
}
