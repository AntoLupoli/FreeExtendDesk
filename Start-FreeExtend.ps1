# Starts FreeExtend: uses the tablet (connected with the USB-C cable) as a second monitor for this PC.
#
# Usage: powershell -NoProfile -File .\Start-FreeExtend.ps1                  (stop with Ctrl+C)
#        powershell -NoProfile -File .\Start-FreeExtend.ps1 -KeepDisplay     (leave the virtual monitor in the desktop at the end)
#        powershell -NoProfile -File .\Start-FreeExtend.ps1 -InstallApp      (rebuild and reinstall the app on the tablet)
#
# What it does: checks the cable and the USB debugging authorization, installs the app on the tablet if it is missing, opens it and
# runs the PC program, which attaches the virtual monitor, streams the screen and detaches it at the end. One-time setup: see README.md.
param([switch]$KeepDisplay, [switch]$InstallApp, [switch]$Yes, [int]$Seconds = 0, [string]$Csv = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
. "$root\tools\env.ps1" | Out-Null

$exe = Join-Path $root 'host\src\FreeExtend.Host\bin\Debug\net8.0-windows\FreeExtend.Host.exe'
if (-not (Test-Path $exe)) {
    Write-Host 'Program not built yet: building it...'
    dotnet build "$root\host\src\FreeExtend.Host" -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

# Laptop with two graphics cards: the NVIDIA encoder only activates if Windows runs the program on the NVIDIA card ("High performance"
# for this executable). We ask before writing that preference (only for this executable).
$gpuCount = @(Get-CimInstance Win32_VideoController |
    Where-Object { $_.Name -and $_.Name -notmatch 'Microsoft Basic|Remote|Virtual Display|Parsec|Meta Virtual|Mirage' }).Count
if ($gpuCount -ge 2) {
    & $exe gpu-status | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "This PC has $gpuCount graphics cards and 'High performance' is not set for this executable."
        $answer = if ($Yes) { 'y' } else { Read-Host "Set it only for `"$exe`" (same as Settings > System > Display > Graphics)? [Y/n]" }
        if ($answer -match '^[nN]') { Write-Host "OK. If the pipeline does not start, run: & `"$exe`" setup-gpu" }
        else { & $exe setup-gpu }
    }
}

# The tablet must be connected and have authorized the PC (install-app.ps1 checks this too and installs the app if it is missing).
& "$root\tools\install-app.ps1" -Force:$InstallApp

# Open the app on the tablet (it connects by itself as soon as the PC program is ready).
& adb shell am start -n it.freeextend.display/.MainActivity | Out-Null

$argsList = @('stream')
if ($KeepDisplay) { $argsList += '--keep-display' }
if ($Seconds -gt 0) { $argsList += @('--seconds', $Seconds) }
if ($Csv) { $argsList += @('--csv', $Csv) }
& $exe @argsList
exit $LASTEXITCODE
