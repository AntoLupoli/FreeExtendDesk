# Stage 0 measurement: starts the app on the tablet, an image source on the virtual monitor and the `stream`
# command for N seconds, then prints the summary. Sources: 'gdi' (PowerShell window, ~33 fps) or 'browser' (60 fps canvas).
# Usage: powershell -NoProfile -File tools\stage0-measure.ps1 -Scenario browser -Seconds 60 -Screen DISPLAY60 -Csv out.csv
param(
    [ValidateSet('gdi', 'browser')][string]$Scenario = 'browser',
    [int]$Seconds = 60,
    [int]$Fps = 60,
    [string]$Screen = '',
    [string]$Csv = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. "$root\tools\env.ps1" | Out-Null
$exe = Join-Path $root 'host\src\FreeExtend.Host\bin\Debug\net8.0-windows\FreeExtend.Host.exe'

# The virtual monitor must be in the desktop before showing the source on it; its number changes on every driver reload.
$null = & $exe display-test attach
if (-not $Screen) { $Screen = (& $exe virtual-output).Trim().TrimStart('\', '.') }

adb shell am force-stop it.freeextend.display | Out-Null
adb shell am start -n it.freeextend.display/.MainActivity | Out-Null

$src = $null; $profileDir = $null
Add-Type -AssemblyName System.Windows.Forms
$scr = [System.Windows.Forms.Screen]::AllScreens | Where-Object { $_.DeviceName.TrimStart('\', '.') -eq $Screen }
if (-not $scr) { throw "Screen $Screen not found" }
if ($Scenario -eq 'gdi') {
    $src = Start-Process powershell -ArgumentList '-NoProfile', '-File', "$root\tools\motion-window.ps1", '-Seconds', ($Seconds + 15), '-Screen', $Screen -PassThru -WindowStyle Hidden
} else {
    $edge = (Get-Command msedge -ErrorAction SilentlyContinue).Source
    if (-not $edge) { $edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe" }
    $profileDir = Join-Path $env:TEMP ("fx-edge-" + [guid]::NewGuid().ToString('N'))
    $url = ([uri](Join-Path $root 'tools\gpu-animation.html')).AbsoluteUri
    $x = $scr.Bounds.X; $y = $scr.Bounds.Y
    $src = Start-Process $edge -ArgumentList "--user-data-dir=`"$profileDir`"", '--no-first-run', '--kiosk', $url, "--window-position=$x,$y", '--edge-kiosk-type=fullscreen' -PassThru
}
Start-Sleep -Seconds 4

$argsList = @("stream", "--seconds", $Seconds, "--fps", $Fps, "--no-cleanup")
if ($Csv) { $argsList += @('--csv', $Csv) }
try { & $exe @argsList } finally {
    if ($src -and -not $src.HasExited) { Stop-Process -Id $src.Id -Force -ErrorAction SilentlyContinue }
    if ($Scenario -eq 'browser') {
        Get-Process msedge -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match 'FreeExtend' } | Stop-Process -Force -ErrorAction SilentlyContinue
        if ($profileDir) { Start-Sleep -Seconds 1; Remove-Item $profileDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
