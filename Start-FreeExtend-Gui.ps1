# Opens the FreeExtend control window (status of the driver, the tablet and the app; Start/Stop streaming; Settings).
#
# Usage: powershell -NoProfile -File .\Start-FreeExtend-Gui.ps1             (builds the window the first time)
#        powershell -NoProfile -File .\Start-FreeExtend-Gui.ps1 -Rebuild    (rebuild it, for example after pulling new code)
param([switch]$Rebuild)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'host\src\FreeExtend.Gui\bin\Debug\net8.0-windows\FreeExtend.exe'

if ($Rebuild -or -not (Test-Path $exe)) {
    Write-Host 'Building the FreeExtend window...'
    dotnet build "$root\host\src\FreeExtend.Gui" -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed (needs the .NET 8 SDK, see README.md).' }
}
Start-Process -FilePath $exe -WorkingDirectory $root
