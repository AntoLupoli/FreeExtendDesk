#Requires -RunAsAdministrator
# Rimuove il Virtual Display Driver (dispositivo + pacchetto driver).
# Uso: PowerShell come amministratore ->  .\tools\virtual-display\remove.ps1
# Alternativa grafica: "VDD Control.exe" -> Uninstall, oppure Gestione dispositivi ->
# Schede video -> Virtual Display Driver -> Disinstalla dispositivo.
$ErrorActionPreference = 'Stop'

$devcon = Join-Path $PSScriptRoot 'vdd-download\extracted\Dependencies\devcon.exe'
if (-not (Test-Path $devcon)) { throw "devcon.exe non trovato: $devcon (riestrarre lo zip, vedi README.md)" }

Write-Host '1) Rimozione del dispositivo Root\MttVDD...'
& $devcon remove 'Root\MttVDD'

Write-Host '2) Rimozione del pacchetto driver...'
$pkgs = Get-WindowsDriver -Online | Where-Object { $_.OriginalFileName -like '*mttvdd.inf' }
if (-not $pkgs) { Write-Host '   nessun pacchetto mttvdd.inf nello store driver.' }
foreach ($p in $pkgs) {
    Write-Host "   pnputil /delete-driver $($p.Driver)"
    pnputil /delete-driver $p.Driver /uninstall /force | Out-Host
}
Write-Host 'Fatto. Se resta un monitor fantasma, riavvia il PC.'
