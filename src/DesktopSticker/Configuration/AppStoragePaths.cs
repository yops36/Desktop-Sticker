using System.Security.Cryptography;

namespace DesktopSticker.Configuration;

/// <summary>
/// Verwaltet die Verzeichnisstruktur im lokalen Anwendungsdatenverzeichnis des Benutzers (Kapitel 5).
/// </summary>
public sealed class AppStoragePaths
{
    public string RootDir { get; }
    public string OriginalsDir { get; }
    public string StickersDir { get; }
    public string GeneratedDir { get; }
    public string SettingsDir { get; }
    public string TempDir { get; }
    public string SettingsFilePath { get; }

    public AppStoragePaths(string? customRootDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(customRootDirectory))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException("Das lokale Anwendungsdatenverzeichnis (LocalApplicationData) konnte nicht ermittelt werden.");
            }

            RootDir = Path.Combine(localAppData, "DesktopSticker");
        }
        else
        {
            RootDir = Path.GetFullPath(customRootDirectory);
        }

        OriginalsDir = Path.Combine(RootDir, "Originals");
        StickersDir = Path.Combine(RootDir, "Stickers");
        GeneratedDir = Path.Combine(RootDir, "Generated");
        SettingsDir = Path.Combine(RootDir, "Settings");
        TempDir = Path.Combine(RootDir, "Temp");
        SettingsFilePath = Path.Combine(SettingsDir, "settings.json");
    }

    /// <summary>
    /// Stellt sicher, dass alle benötigten Unterordner existieren und beschreibbar sind (Kapitel 5 & 18.5).
    /// </summary>
    public void EnsureDirectoriesAndWriteAccess()
    {
        try
        {
            Directory.CreateDirectory(RootDir);
            Directory.CreateDirectory(OriginalsDir);
            Directory.CreateDirectory(StickersDir);
            Directory.CreateDirectory(GeneratedDir);
            Directory.CreateDirectory(SettingsDir);
            Directory.CreateDirectory(TempDir);

            // Schreibbarkeit aktiv prüfen (Kapitel 18.5)
            string probeFile = Path.Combine(TempDir, $"write_probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probeFile, "ok");
            File.Delete(probeFile);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Das Anwendungsdatenverzeichnis („{RootDir}“) konnte nicht erstellt oder beschrieben werden. Bitte prüfen Sie die Ordnerberechtigungen.",
                ex);
        }
    }

    /// <summary>
    /// Bereinigt liegengebliebene temporäre Dateien eines eventuell abgebrochenen früheren Vorgangs (Kapitel 18.7).
    /// </summary>
    public void CleanupTempDirectory()
    {
        try
        {
            if (!Directory.Exists(TempDir))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(TempDir))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Einzelne gesperrte Datei ignorieren
                }
            }
        }
        catch
        {
            // Ignorieren
        }
    }

    /// <summary>
    /// Prüft, ob ein Dateipfad innerhalb des Ordners für generierte Hintergrundbilder liegt.
    /// </summary>
    public bool IsPathInsideGeneratedDirectory(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(filePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string genDir = Path.GetFullPath(GeneratedDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (fullPath.StartsWith(genDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullPath, genDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string fileName = Path.GetFileName(fullPath);
            return fileName.StartsWith("DesktopSticker_Generated_", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Berechnet den SHA-256-Hash einer Datei zur eindeutigen Identifikation (Schutzregel Kapitel 5).
    /// </summary>
    public static string ComputeFileSha256(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
