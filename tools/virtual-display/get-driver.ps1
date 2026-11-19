# Scarica e prepara il Virtual Display Driver (https://github.com/VirtualDrivers/Virtual-Display-Driver, licenza MIT, firmato SignPath).
# Non installa nulla: mette i file in tools\virtual-display\vdd-download\extracted\, che poi usa install.ps1 (come amministratore).
#
# Uso:  powershell -NoProfile -File .\tools\virtual-display\get-driver.ps1
#
# Sicurezza: lo zip si accetta solo se l'hash SHA256 coincide con quello fissato qui (lo stesso del manifest winget del progetto) e
# se i file del driver hanno una firma valida di SignPath Foundation. Se non coincide, lo zip si cancella e lo script si ferma.
# Se vuoi una versione diversa passa -Url e -Sha256 (e verifica tu l'origine).
param(
    [string]$Url = 'https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VDD.Control.25.7.23.zip',
    [string]$Sha256 = 'A701F2272E9FCF382849B24F913C6DD07597B3B1116525F2E90182F019609154',
    [string]$Dir = (Join-Path $PSScriptRoot 'vdd-download'),
    [switch]$Force,
    [switch]$SkipSignatureCheck     # solo per i test con un archivio finto
)
$ErrorActionPreference = 'Stop'

$zip       = Join-Path $Dir (Split-Path $Url -Leaf)
$extracted = Join-Path $Dir 'extracted'
$needed    = @('Dependencies\devcon.exe', 'SignedDrivers\x86\VDD\MttVDD.inf')

function Test-Prepared { -not ($needed | Where-Object { -not (Test-Path (Join-Path $extracted $_)) }) }

if (-not $Force -and (Test-Prepared)) {
    Write-Host "Driver gia pronto in $extracted"
    return
}

New-Item -ItemType Directory -Force $Dir | Out-Null

if ($Force -or -not (Test-Path $zip)) {
    Write-Host "Scarico $Url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $tmp = "$zip.part"
    Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing
    Move-Item $tmp $zip -Force
}
Write-Host ("Archivio: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

$actual = (Get-FileHash $zip -Algorithm SHA256).Hash
if ($actual -ne $Sha256.ToUpperInvariant()) {
    Remove-Item $zip -Force
    throw "Hash SHA256 diverso da quello atteso: l'archivio e stato cancellato.`n  atteso:  $Sha256`n  ottenuto: $actual"
}
Write-Host 'Hash SHA256: OK'

if (Test-Path $extracted) { Remove-Item $extracted -Recurse -Force }
Expand-Archive $zip -DestinationPath $extracted -Force
if (-not (Test-Prepared)) { throw "Archivio diverso dal previsto: mancano file in $extracted" }

if (-not $SkipSignatureCheck) {
    foreach ($rel in 'SignedDrivers\x86\VDD\MttVDD.dll', 'SignedDrivers\x86\VDD\mttvdd.cat') {
        $s = Get-AuthenticodeSignature (Join-Path $extracted $rel)
        if ($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'SignPath Foundation') {
            throw "Firma non valida su ${rel}: $($s.Status) / $($s.SignerCertificate.Subject)"
        }
    }
    Write-Host 'Firme SignPath Foundation: OK'
}
Write-Host "Driver pronto in $extracted. Ora installalo con install.ps1 (PowerShell come amministratore)."
