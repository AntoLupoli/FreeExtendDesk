# Installa il Virtual Display Driver (solo il driver video) con devcon, senza usare VDD Control, e ne scrive la configurazione.
# Uso (PowerShell come amministratore):
#   powershell -ExecutionPolicy Bypass -File .\tools\virtual-display\install.ps1
#   ... -Gpu "NVIDIA GeForce RTX 3050 Laptop GPU"   scheda che ospita il monitor virtuale (stessa dell'encoder); di solito si sceglie da sola
#   ... -ExtraModes 1920x1080,2560x1440             risoluzioni in piu per il tuo tablet (il driver ne offre di base tre, tutte 16:10)
#   ... -ConfigOnly [-OutFile file.xml]             scrive solo la configurazione, senza installare (non serve l'amministratore)
#
# Cosa fa, in ordine:
#   1. scarica il driver se manca (get-driver.ps1: hash SHA256 e firma SignPath verificati);
#   2. sceglie la scheda grafica e scrive la configurazione in C:\VirtualDisplayDriver\vdd_settings.xml;
#   3. installa il dispositivo Root\MttVDD con devcon.exe (firmato Microsoft);
#   4. stampa schermi e dispositivo risultanti.
# Non tocca test signing, Secure Boot né il driver audio. Rimozione: remove.ps1.
param(
    [string]$Gpu = '',
    [string[]]$ExtraModes = @(),
    [switch]$ConfigOnly,
    [string]$OutFile = ''
)
$ErrorActionPreference = 'Stop'

$template = Join-Path $PSScriptRoot 'vdd_settings.freeextend.xml'
if (-not (Test-Path $template)) { throw "File mancante: $template" }

# --- scheda grafica ------------------------------------------------------------------------------
function Get-GraphicsAdapters {
    @(Get-CimInstance Win32_VideoController |
        Where-Object { $_.Name -and $_.Name -notmatch 'Microsoft Basic|Remote|Virtual Display|Parsec|Meta Virtual|Mirage' } |
        ForEach-Object { $_.Name } | Select-Object -Unique)
}

function Select-Gpu([string]$wanted, [string[]]$adapters, [bool]$interactive) {
    if ($wanted) { return $wanted }
    if ($adapters.Count -eq 0) { throw 'Nessuna scheda grafica trovata: indica quella da usare con -Gpu "nome".' }
    if ($adapters.Count -eq 1) { return $adapters[0] }
    $nvidia = @($adapters | Where-Object { $_ -match 'NVIDIA' })
    if ($nvidia.Count -ge 1) { return $nvidia[0] }          # NVENC e l'encoder piu rapido provato; vedi README (portatili a due schede)
    if (-not $interactive) { return $adapters[0] }
    Write-Host 'Piu schede grafiche: su quale deve girare il monitor virtuale (e la codifica)?'
    for ($i = 0; $i -lt $adapters.Count; $i++) { Write-Host ("  {0}) {1}" -f ($i + 1), $adapters[$i]) }
    $n = [int](Read-Host 'Numero')
    if ($n -lt 1 -or $n -gt $adapters.Count) { throw 'Scelta non valida.' }
    return $adapters[$n - 1]
}

$adapters = Get-GraphicsAdapters
$gpuName = Select-Gpu $Gpu $adapters (-not $ConfigOnly)
Write-Host "Scheda per il monitor virtuale: $gpuName"

# --- configurazione ------------------------------------------------------------------------------
[xml]$xml = Get-Content $template -Raw
$xml.vdd_settings.gpu.friendlyname = [string]$gpuName
foreach ($m in $ExtraModes) {
    if ($m -notmatch '^(\d{3,5})x(\d{3,5})$') { throw "Risoluzione non valida '$m': usa il formato LARGHEZZAxALTEZZA, per esempio 1920x1080." }
    $r = $xml.CreateElement('resolution')
    foreach ($pair in @(@('width', $Matches[1]), @('height', $Matches[2]), @('refresh_rate', '60'))) {
        $e = $xml.CreateElement($pair[0]); $e.InnerText = $pair[1]; [void]$r.AppendChild($e)
    }
    [void]$xml.vdd_settings.resolutions.AppendChild($r)
}

if ($ConfigOnly) {
    $target = if ($OutFile) { $OutFile } else { Join-Path $PSScriptRoot 'vdd_settings.generated.xml' }
    $xml.Save($target)
    Write-Host "Configurazione scritta in $target (nessuna installazione)."
    return
}

# --- installazione (serve l'amministratore) ------------------------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Serve PowerShell come amministratore (tasto destro > Esegui come amministratore).' }

& (Join-Path $PSScriptRoot 'get-driver.ps1')

$root   = Join-Path $PSScriptRoot 'vdd-download\extracted'
$drvDir = Join-Path $root 'SignedDrivers\x86\VDD'     # (cartella "x86" ma contiene il driver x64, INF NTamd64)
$devcon = Join-Path $root 'Dependencies\devcon.exe'
$inf    = Join-Path $drvDir 'MttVDD.inf'
foreach ($p in $devcon, $inf) { if (-not (Test-Path $p)) { throw "File mancante: $p" } }

Write-Host '1) Verifica firme...'
foreach ($f in 'MttVDD.dll', 'mttvdd.cat') {
    $s = Get-AuthenticodeSignature (Join-Path $drvDir $f)
    if ($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'SignPath Foundation') {
        throw "Firma non valida su ${f}: $($s.Status) / $($s.SignerCertificate.Subject)"
    }
    Write-Host "   $f OK ($($s.SignerCertificate.Subject -replace ',.*$',''))"
}

Write-Host '2) Configurazione in C:\VirtualDisplayDriver ...'
New-Item -ItemType Directory -Force 'C:\VirtualDisplayDriver' | Out-Null
$xml.Save('C:\VirtualDisplayDriver\vdd_settings.xml')

Write-Host '3) Installazione del dispositivo Root\MttVDD ...'
& $devcon install $inf 'Root\MttVDD'
if ($LASTEXITCODE -ne 0) { throw "devcon ha restituito il codice $LASTEXITCODE (vedi messaggio sopra)" }

Write-Host '4) Risultato:'
Start-Sleep -Seconds 3
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Screen]::AllScreens | ForEach-Object { "   $($_.DeviceName) $($_.Bounds.Width)x$($_.Bounds.Height) primary=$($_.Primary)" }
Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | Select-Object Status, FriendlyName | Format-Table -AutoSize | Out-String | Write-Host
Write-Host 'Il nuovo monitor compare "non attaccato": FreeExtend lo attacca da solo quando il tablet si connette.'
