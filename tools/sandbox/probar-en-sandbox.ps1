# Prueba completa de OpenSweep dentro de Windows Sandbox (una máquina desechable).
# Uso, desde la carpeta del repositorio:
#   powershell -ExecutionPolicy Bypass -File tools\sandbox\probar-en-sandbox.ps1           (prueba rápida, ~3 min)
#   powershell -ExecutionPolicy Bypass -File tools\sandbox\probar-en-sandbox.ps1 -Full     (incluye cleanmgr y DISM)
# Requisito: Windows 10/11 Pro, Education o Enterprise con «Espacio aislado de Windows» activado.
param([switch]$Full, [string]$Exe)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
if (-not $Exe) { $Exe = Join-Path $repo 'dist\OpenSweep-windows.exe' }
if (-not (Test-Path $Exe)) {
    Write-Host "No encuentro $Exe. Publica antes el exe portable (ver CONTRIBUTING.md) o pasa -Exe <ruta>." -ForegroundColor Red
    exit 1
}
if (-not (Get-Command WindowsSandbox.exe -ErrorAction SilentlyContinue)) {
    Write-Host 'Windows Sandbox no está activado. Abre PowerShell como administrador y ejecuta:' -ForegroundColor Yellow
    Write-Host '  Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All'
    Write-Host 'Después reinicia el PC y vuelve a lanzar este script.'
    exit 1
}

# Carpeta de trabajo: la app (solo lectura dentro del sandbox) y los resultados (escritura)
$work = Join-Path $env:TEMP 'OpenSweepSandbox'
$app = Join-Path $work 'app'
$results = Join-Path $repo 'dist\pruebas-sandbox'
New-Item -ItemType Directory -Force $app, $results | Out-Null
Copy-Item $Exe (Join-Path $app 'OpenSweep.exe') -Force
Copy-Item (Join-Path $PSScriptRoot 'dentro-del-sandbox.ps1') $app -Force

$flag = if ($Full) { ' -Full' } else { '' }
$wsb = @"
<Configuration>
  <Networking>Enable</Networking>
  <MappedFolders>
    <MappedFolder><HostFolder>$app</HostFolder><SandboxFolder>C:\Prueba\app</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$results</HostFolder><SandboxFolder>C:\Prueba\resultados</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -ExecutionPolicy Bypass -File C:\Prueba\app\dentro-del-sandbox.ps1$flag</Command>
  </LogonCommand>
</Configuration>
"@
$wsbFile = Join-Path $work 'OpenSweep.wsb'
Set-Content -Path $wsbFile -Value $wsb -Encoding UTF8
Write-Host "Abriendo Windows Sandbox. El informe aparecerá en: $results" -ForegroundColor Green
Start-Process $wsbFile
