<#
.SYNOPSIS
    Erzeugt den Windows-Installer deploy\output\DesktopSticker-Setup.exe.

.DESCRIPTION
    1. Sucht .NET-SDK und Inno Setup (zuerst im lokalen Ordner .tools\, dann im PATH / Standardpfad).
    2. Fuehrt die automatisierte Test-Suite aus (Abbruch bei Fehlern).
    3. Veroeffentlicht die App eigenstaendig (self-contained, win-x64, inkl. .NET-Runtime) als Single-File-EXE.
    4. Kompiliert mit Inno Setup einen echten Windows-Installer (deploy\DesktopSticker.iss).
    5. Schreibt die SHA-256-Pruefsumme nach deploy\output\SHA256SUMS.txt.

.PARAMETER SkipTests
    Ueberspringt die Test-Suite (nicht fuer Veroeffentlichungen empfohlen).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deploy\build-release.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot    = Split-Path -Parent $PSScriptRoot
$ToolsDir    = Join-Path $RepoRoot '.tools'
$AppProject  = Join-Path $RepoRoot 'src\DesktopSticker\DesktopSticker.csproj'
$TestProject = Join-Path $RepoRoot 'tests\DesktopSticker.Tests\DesktopSticker.Tests.csproj'
$IssScript   = Join-Path $PSScriptRoot 'DesktopSticker.iss'
$PublishDir  = Join-Path $PSScriptRoot 'obj\publish'
$OutputDir   = Join-Path $PSScriptRoot 'output'
$SetupExe    = Join-Path $OutputDir 'DesktopSticker-Setup.exe'

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Find-Tool([string[]]$Candidates, [string]$CommandName) {
    foreach ($c in $Candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return (Resolve-Path -LiteralPath $c).Path }
    }
    $cmd = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

# ---------------------------------------------------------------------------
# 1. Werkzeuge finden
# ---------------------------------------------------------------------------
Write-Step 'Build-Werkzeuge suchen'
$Dotnet = Find-Tool @((Join-Path $ToolsDir 'dotnet\dotnet.exe')) 'dotnet'
if (-not $Dotnet) { throw '.NET 10 SDK nicht gefunden: https://dotnet.microsoft.com/download' }

$Iscc = Find-Tool @(
    (Join-Path $ToolsDir 'InnoSetup\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) 'iscc'
if (-not $Iscc) { throw 'Inno Setup 6 nicht gefunden: https://jrsoftware.org/isdl.php' }

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Write-Host "dotnet : $Dotnet ($(& $Dotnet --version))"
Write-Host "ISCC   : $Iscc"

[xml]$csproj = Get-Content -LiteralPath $AppProject -Raw
$Version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $Version) { throw "Keine <Version> in $AppProject gefunden." }
Write-Host "Version: $Version"

# ---------------------------------------------------------------------------
# 2. Tests
# ---------------------------------------------------------------------------
if ($SkipTests) {
    Write-Warning 'Tests wurden uebersprungen (-SkipTests).'
}
else {
    Write-Step 'Test-Suite ausfuehren'
    & $Dotnet run --project $TestProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests fehlgeschlagen (Exit-Code $LASTEXITCODE). Abbruch." }
}

# ---------------------------------------------------------------------------
# 3. Self-contained Publish
# ---------------------------------------------------------------------------
Write-Step 'App eigenstaendig veroeffentlichen (win-x64, inkl. .NET-Runtime)'
foreach ($dir in @($PublishDir, $OutputDir)) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

& $Dotnet publish $AppProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $PublishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen (Exit-Code $LASTEXITCODE)." }

$BuiltExe = Join-Path $PublishDir 'DesktopSticker.exe'
if (-not (Test-Path -LiteralPath $BuiltExe)) { throw "Erwartete Datei nicht gefunden: $BuiltExe" }

# ---------------------------------------------------------------------------
# 4. Installer kompilieren
# ---------------------------------------------------------------------------
Write-Step 'Windows-Installer mit Inno Setup erstellen'
& $Iscc "/DAppVersion=$Version" "/DPublishDir=$PublishDir" $IssScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup fehlgeschlagen (Exit-Code $LASTEXITCODE)." }
if (-not (Test-Path -LiteralPath $SetupExe)) { throw "Installer wurde nicht erzeugt: $SetupExe" }

# ---------------------------------------------------------------------------
# 5. Pruefsumme
# ---------------------------------------------------------------------------
$hash = (Get-FileHash -LiteralPath $SetupExe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  DesktopSticker-Setup.exe" | Set-Content -LiteralPath (Join-Path $OutputDir 'SHA256SUMS.txt') -Encoding ASCII

Remove-Item -LiteralPath $PublishDir -Recurse -Force

Write-Step 'Fertig'
Write-Host ("Installer : {0} ({1:N1} MB)" -f $SetupExe, ((Get-Item -LiteralPath $SetupExe).Length / 1MB)) -ForegroundColor Green
Write-Host "SHA-256   : $hash" -ForegroundColor Green
