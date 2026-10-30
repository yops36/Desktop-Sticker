; ============================================================================
; Desktop Sticker – Windows-Installer (Inno Setup 6)
; Erzeugt: deploy\output\DesktopSticker-Setup.exe
; Wird von deploy\build-release.ps1 aufgerufen (übergibt AppVersion & PublishDir).
; ============================================================================

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "obj\publish"
#endif

#define AppName      "Desktop Sticker"
#define AppExeName   "DesktopSticker.exe"
#define AppPublisher "Desktop Sticker"

[Setup]
; Feste AppId – NIEMALS ändern, sonst erkennt Windows Updates nicht als dieselbe App.
AppId={{6F3B2A91-4C7E-4D1A-9B8E-2E5F7C1D0A42}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

; Installation pro Benutzer – keine Administratorrechte nötig
; ({autopf} = %LOCALAPPDATA%\Programs bei PrivilegesRequired=lowest)
PrivilegesRequired=lowest
DefaultDirName={autopf}\DesktopSticker
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

; Nur Windows 10/11 x64
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Ausgabe
OutputDir=output
OutputBaseFilename=DesktopSticker-Setup
SetupIconFile=..\App Icon\Blaues-W-mit-Bildsymbol_1_.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
de.DeleteUserData=Sollen auch Ihre Desktop-Sticker-Einstellungen und gespeicherten Bilder gelöscht werden?%n%n(AppData\Local\DesktopSticker)
en.DeleteUserData=Do you also want to delete your Desktop Sticker settings and saved images?%n%n(AppData\Local\DesktopSticker)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Nach einem Update: bereits aktive Sticker wieder einblenden (nur im interaktiven Modus)
Filename: "{app}\{#AppExeName}"; Parameters: "--overlay"; Flags: nowait runhidden skipifsilent
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Overlay beenden, Autostart entfernen und ggf. Original-Hintergrundbild wiederherstellen
Filename: "{app}\{#AppExeName}"; Parameters: "--deactivate"; RunOnceId: "DeactivateSticker"; Flags: runhidden waituntilterminated

[Code]
const
  EVENT_MODIFY_STATE = $0002;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

{ Signalisiert einem laufenden Overlay-Prozess, sich geordnet zu beenden (gleiche Events wie OverlayHostManager.cs). }
procedure SignalOverlayShutdown(const EventName: string);
var
  H: THandle;
begin
  H := OpenEvent(EVENT_MODIFY_STATE, False, EventName);
  if H <> 0 then
  begin
    SetEvent(H);
    CloseHandle(H);
  end;
end;

procedure StopRunningOverlay();
begin
  SignalOverlayShutdown('Local\DesktopSticker_OverlayShutdown_Event_v2');
  SignalOverlayShutdown('Local\DesktopSticker_OverlayShutdown_Event_v1');
  Sleep(1500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { Bei Updates muss die Programmdatei frei sein }
  StopRunningOverlay();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopRunningOverlay();
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { Von der App angelegte Benutzer-Registrierungseinträge entfernen }
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\DesktopBackground\Shell\DesktopStickerEdit');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\DesktopBackground\Shell\DesktopStickerDeactivate');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DesktopSticker');
    { Eintrag des früheren eingebauten Installationsmodus (falls vorhanden) }
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopSticker');

    { Benutzerdaten nur auf ausdrücklichen Wunsch löschen (bei stiller Deinstallation immer behalten) }
    if (not UninstallSilent()) and
       (MsgBox(ExpandConstant('{cm:DeleteUserData}'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    begin
      DelTree(ExpandConstant('{localappdata}\DesktopSticker'), True, True, True);
    end;
  end;
end;
