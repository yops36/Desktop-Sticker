# Desktop Sticker (Version 1.1.0)

**Desktop Sticker** ist ein kleines, lokales Windows-Werkzeug, mit dem Sie statische Bilder (**PNG, JPG, JPEG**) sowie **statische und animierte GIFs** frei auf Ihrem Desktop platzieren können – direkt über dem Hintergrundbild, unterhalb der Desktop-Symbole.

---

## Funktionsprinzip

Die ausgewählten Bilder werden in einen lokalen Anwendungsdatenordner kopiert und von einem schlanken, unsichtbaren Overlay-Prozess (`DesktopSticker.exe --overlay`) auf der Desktop-Ebene dargestellt. Dadurch funktionieren auch animierte GIFs sowie Live-Wallpaper-Programme (z. B. Wallpaper Engine).

- **Kein Internetzugriff, keine Telemetrie, kein Benutzerkonto**
- **Keine Administratorrechte erforderlich** – alle Änderungen erfolgen ausschließlich im Benutzerprofil (`HKCU` bzw. `%LOCALAPPDATA%`)
- **Sichere Wiederherstellung** eines zuvor gesicherten Original-Hintergrundbildes jederzeit möglich
- **Vollständige Deinstallation** über *Windows-Einstellungen → Apps → Installierte Apps*

> [!NOTE]
> **Transparenzhinweis:** Solange mindestens ein Sticker aktiv ist, läuft der Overlay-Prozess im Hintergrund, und es wird ein Autostart-Eintrag unter
> `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (Wert `DesktopSticker`) angelegt, damit die Sticker nach der Anmeldung wieder erscheinen.
> Beim Deaktivieren oder Deinstallieren werden Overlay-Prozess und Autostart-Eintrag wieder entfernt.

---

## Funktionen

- **Bildformate:** `.png` (inkl. Alphatransparenz), `.jpg`, `.jpeg` (inkl. automatischer EXIF-Orientierungskorrektur) sowie `.gif` (statisch & animiert)
- **Mehrere Sticker gleichzeitig** (Bilderstapel)
- **Proportionale Größenänderung:** per Schieberegler oder Zahlenfeld
- **Freie Positionierung:** per Drag & Drop in der Vorschau oder präzise über X-/Y-Zahlenfelder
- **Eckenradius:** optionales, kantengeglättetes Abrunden der Bildecken
- **Sprachen:** Deutsch und Englisch
- **Optionale Desktop-Kontextmenüintegration:** Rechtsklick auf den Desktop → *„Desktop Sticker bearbeiten“* / *„Desktop Sticker deaktivieren“*
- **Eingebauter 1-Klick-Installer** (ohne Administratorrechte)

### Sicherheitsgrenzen beim Bildimport

| Grenze | Wert |
| --- | --- |
| Maximale Dateigröße | 50 MB |
| Maximale Kantenlänge | 8192 px |
| Maximale Gesamtpixel | 36 Megapixel |
| Maximale GIF-Frames | 500 |

---

## Installation (für Anwender)

1. Laden Sie unter **[Releases](../../releases)** die Datei `DesktopSticker-Setup.exe` herunter.
2. *(Empfohlen)* Prüfen Sie die SHA-256-Prüfsumme gegen `SHA256SUMS.txt` aus demselben Release:
   ```powershell
   Get-FileHash .\DesktopSticker-Setup.exe -Algorithm SHA256
   ```
3. Starten Sie `DesktopSticker-Setup.exe` und klicken Sie auf **„Installieren“**.
   Das Programm wird nach `%LOCALAPPDATA%\Programs\DesktopSticker\` installiert.

> [!IMPORTANT]
> Die Programmdatei ist nicht digital signiert. Windows SmartScreen kann daher beim ersten Start eine Warnung anzeigen
> (*„Weitere Informationen“ → „Trotzdem ausführen“*). Vergleichen Sie im Zweifel die Prüfsumme oder bauen Sie das Programm selbst aus dem Quellcode.

### Portable Nutzung

Alternativ kann `DesktopSticker.exe` (ebenfalls im Release enthalten) ohne Installation direkt gestartet werden.

### Kommandozeilenoptionen

| Befehl | Wirkung |
| --- | --- |
| `DesktopSticker.exe` / `--edit` | Öffnet das Bearbeitungsfenster |
| `DesktopSticker.exe --setup` / `--install` | Öffnet den Installationsassistenten (startet auch automatisch, wenn der Dateiname „setup“ oder „install“ enthält) |
| `DesktopSticker.exe --deactivate` | Beendet das Overlay, entfernt den Autostart-Eintrag und stellt ggf. das Original-Hintergrundbild wieder her |
| `DesktopSticker.exe --uninstall` | Entfernt Overlay, Autostart, Kontextmenü, Verknüpfungen und den Eintrag in „Installierte Apps“; löscht auf Wunsch die Anwendungsdaten unter `%LOCALAPPDATA%\DesktopSticker` |

> [!NOTE]
> Die Programmdatei selbst (`%LOCALAPPDATA%\Programs\DesktopSticker\DesktopSticker.exe`) kann sich während der Ausführung nicht selbst löschen. Entfernen Sie diesen Ordner nach der Deinstallation bei Bedarf manuell.

---

## Aus dem Quellcode bauen

Voraussetzungen: Windows 10/11 (x64) und das [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
# Tests ausführen
dotnet run --project tests/DesktopSticker.Tests -c Release

# Release-Dateien erzeugen (Setup, portable EXE, ZIP, Prüfsummen)
powershell -ExecutionPolicy Bypass -File deploy/build-release.ps1
```

Details zur Veröffentlichung auf GitHub finden Sie in [`deploy/README.md`](deploy/README.md).

---

## Projektstruktur

```text
DesktopSticker.slnx
├── src/DesktopSticker/
│   ├── Program.cs              # Einstiegspunkt & Kommandozeilenparameter
│   ├── app.manifest            # asInvoker (keine Adminrechte), PerMonitorV2-DPI
│   ├── Backup/                 # Sicherung & Wiederherstellung des Original-Hintergrunds
│   ├── Configuration/          # Einstellungen (JSON, atomares Speichern) & Speicherpfade
│   ├── Imaging/                # Bildprüfung, EXIF-Korrektur, GIF-Frames, Geometrie, Eckenradius
│   ├── Integration/            # Kontextmenü, Autostart, Single-Instance, Deinstallation
│   ├── Localization/           # Übersetzungen (de/en) als eingebettete Ressourcen
│   ├── Overlay/                # Overlay-Hostprozess & Darstellungsfenster
│   ├── UI/                     # Hauptfenster, Vorschau, Setup-Assistent, Sprachauswahl, Theme
│   └── Wallpaper/              # IDesktopWallpaper-COM-Interop & Wallpaper-Dienst
├── tests/DesktopSticker.Tests/ # Automatisierte Test-Suite
└── deploy/                     # Release-Skript & Veröffentlichungsanleitung
```

---

## Datenschutz

Desktop Sticker sendet keinerlei Daten. Gespeichert werden ausschließlich lokal:

- `%LOCALAPPDATA%\DesktopSticker\` – Einstellungen, Kopien der ausgewählten Bilder, Hintergrund-Sicherungen
- `HKCU\Software\Classes\DesktopBackground\Shell\DesktopSticker*` – optionale Kontextmenüeinträge
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DesktopSticker` – Autostart, solange Sticker aktiv sind
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopSticker` – Eintrag in „Installierte Apps“ (nur nach Installation)

## Lizenz

Veröffentlicht unter der [MIT-Lizenz](LICENSE).
