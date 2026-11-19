# Runs encode-test with the animated window on the virtual monitor, using FreeExtend.Host.exe directly
# (the Windows GPU preference is bound to the executable's path, not to "dotnet run").
# Usage: tools\encode-bench.ps1 -Screen DISPLAY59 [-Seconds 8] [-ExtraArgs '--quality 0']
param([string]$Screen = 'DISPLAY59', [int]$Seconds = 8, [string]$ExtraArgs = '')
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'host\src\FreeExtend.Host\bin\Debug\net8.0-windows\FreeExtend.Host.exe'
if (-not (Test-Path $exe)) { throw "Not found: $exe (run dotnet build host\src\FreeExtend.Host first)" }
$p = Start-Process powershell -ArgumentList '-NoProfile','-File',"$root\tools\motion-window.ps1",'-Seconds',($Seconds + 8),'-Screen',$Screen -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
try { & $exe encode-test --output $Screen --seconds $Seconds @($ExtraArgs -split '\s+' | Where-Object { $_ }) } finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
