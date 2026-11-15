# Se ejecuta automáticamente dentro de Windows Sandbox (no lo lances en tu PC: la app se niega igualmente).
param([switch]$Full)
$exe = 'C:\Users\WDAGUtilityAccount\Desktop\OpenSweep.exe'
Copy-Item 'C:\Prueba\app\OpenSweep.exe' $exe -Force   # copia local: las tareas programadas necesitan una ruta estable
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$report = "C:\Prueba\resultados\informe_$stamp.txt"
$argsList = @('--selftest', $report)
if ($Full) { $argsList += '--full' }
$p = Start-Process -FilePath $exe -ArgumentList $argsList -Wait -PassThru
Add-Content -Path $report -Value "Código de salida: $($p.ExitCode)" -Encoding UTF8
Start-Process notepad.exe $report
# Para seguir probando a mano (desinstalar, limpiar, la bandeja…), la app queda abierta
Start-Process $exe
