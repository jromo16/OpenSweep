# Genera el manifiesto de winget para una versión YA publicada en GitHub Releases.
# Uso: powershell -ExecutionPolicy Bypass -File packaging\winget\generar-manifiesto.ps1 -Version 1.2.0
# Después: crea un Pull Request en https://github.com/microsoft/winget-pkgs con la carpeta generada
# (o usa «wingetcreate submit <carpeta>»). Ver packaging/README.md.
param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
$id = 'jromo16.OpenSweep'
$url = "https://github.com/jromo16/OpenSweep/releases/download/v$Version/OpenSweep-Setup.exe"
$out = Join-Path $PSScriptRoot "manifests\j\jromo16\OpenSweep\$Version"
New-Item -ItemType Directory -Force $out | Out-Null

$tmp = Join-Path $env:TEMP "OpenSweep-Setup-$Version.exe"
Write-Host "Descargando $url"
Invoke-WebRequest $url -OutFile $tmp -UseBasicParsing
$sha = (Get-FileHash $tmp -Algorithm SHA256).Hash
Remove-Item $tmp

$schema = '1.10.0'
$utf8 = New-Object System.Text.UTF8Encoding($false)

[IO.File]::WriteAllText("$out\$id.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@, $utf8)

[IO.File]::WriteAllText("$out\$id.installer.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: inno
Scope: machine
ElevationRequirement: elevatesSelf
UpgradeBehavior: install
ProductCode: '{6F3C2A1E-8B5D-4E7A-9C21-0A5B7E4D3F10}_is1'
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema
"@, $utf8)

[IO.File]::WriteAllText("$out\$id.locale.en-US.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: jromo16
PublisherUrl: https://github.com/jromo16
PublisherSupportUrl: https://github.com/jromo16/OpenSweep/issues
PackageName: OpenSweep
PackageUrl: https://github.com/jromo16/OpenSweep
License: MIT
LicenseUrl: https://github.com/jromo16/OpenSweep/blob/main/LICENSE
ShortDescription: Free and open-source PC cleaner and uninstaller.
Description: OpenSweep cleans junk files, browsers and the registry, uninstalls programs together with their leftovers, finds duplicate files and photos, analyzes disk usage and adjusts Windows privacy settings. No ads, no telemetry.
Tags:
- cleaner
- uninstaller
- privacy
- disk-analyzer
- duplicates
- open-source
ReleaseNotesUrl: https://github.com/jromo16/OpenSweep/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@, $utf8)

Write-Host "Manifiesto creado en $out" -ForegroundColor Green
Write-Host "Valídalo con: winget validate --manifest `"$out`""
