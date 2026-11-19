# Esegue encode-test con la finestra animata sul monitor virtuale, usando direttamente FreeExtend.Host.exe
# (la preferenza GPU di Windows si lega al percorso dell'eseguibile, non a "dotnet run").
# Uso: tools\encode-bench.ps1 -Screen DISPLAY59 [-Seconds 8] [-ExtraArgs '--quality 0']
param([string]$Screen = 'DISPLAY59', [int]$Seconds = 8, [string]$ExtraArgs = '')
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'host\src\FreeExtend.Host\bin\Debug\net8.0-windows\FreeExtend.Host.exe'
if (-not (Test-Path $exe)) { throw "Non trovato: $exe (esegui prima dotnet build host\src\FreeExtend.Host)" }
$p = Start-Process powershell -ArgumentList '-NoProfile','-File',"$root\tools\motion-window.ps1",'-Seconds',($Seconds + 8),'-Screen',$Screen -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
try { & $exe encode-test --output $Screen --seconds $Seconds @($ExtraArgs -split '\s+' | Where-Object { $_ }) } finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
