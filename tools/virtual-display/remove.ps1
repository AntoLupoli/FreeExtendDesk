#Requires -RunAsAdministrator
# Removes the Virtual Display Driver (device + driver package) and the configuration folder that install.ps1 created.
# Usage: PowerShell as administrator ->  .\tools\virtual-display\remove.ps1
# Graphical alternative: "VDD Control.exe" -> Uninstall, or Device Manager -> Display adapters -> Virtual Display Driver -> Uninstall device.
$ErrorActionPreference = 'Stop'

$devcon = Join-Path $PSScriptRoot 'vdd-download\extracted\Dependencies\devcon.exe'
if (-not (Test-Path $devcon)) {
    Write-Host 'devcon.exe not found: downloading the (verified) driver package first...'
    & (Join-Path $PSScriptRoot 'get-driver.ps1')
}
if (-not (Test-Path $devcon)) { throw "devcon.exe not found: $devcon (see README.md)" }

Write-Host '1) Removing the Root\MttVDD device...'
& $devcon remove 'Root\MttVDD'

Write-Host '2) Removing the driver package...'
$pkgs = Get-WindowsDriver -Online | Where-Object { $_.OriginalFileName -like '*mttvdd.inf' }
if (-not $pkgs) { Write-Host '   no mttvdd.inf package in the driver store.' }
foreach ($p in $pkgs) {
    Write-Host "   pnputil /delete-driver $($p.Driver)"
    pnputil /delete-driver $p.Driver /uninstall /force | Out-Host
}

Write-Host '3) Removing the configuration folder C:\VirtualDisplayDriver...'
if (Test-Path 'C:\VirtualDisplayDriver') { Remove-Item 'C:\VirtualDisplayDriver' -Recurse -Force }

Write-Host 'Done. If a ghost monitor remains, restart the PC.'
