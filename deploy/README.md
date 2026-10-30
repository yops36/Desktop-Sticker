# Veröffentlichung auf GitHub

Im Ordner `deploy\output\` liegt der fertige Windows-Installer:

- **`DesktopSticker-Setup.exe`** – vollständiger Windows-Installer (Inno Setup 6, Windows 10/11 x64, *self-contained* inkl. .NET 10 Runtime)
- **`SHA256SUMS.txt`** – SHA-256-Prüfsumme der Setup-Datei

## Installer neu bauen

Falls Sie Änderungen am Quellcode vornehmen, können Sie den Installer jederzeit neu erzeugen:

```powershell
powershell -ExecutionPolicy Bypass -File deploy\build-release.ps1
```

*(Die benötigten Build-Werkzeuge `.NET 10 SDK` und `Inno Setup 6` sind bereits lokal im Ordner `.tools\` eingerichtet und werden automatisch verwendet.)*
