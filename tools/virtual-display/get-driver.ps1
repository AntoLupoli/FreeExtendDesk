# Downloads and prepares the Virtual Display Driver (https://github.com/VirtualDrivers/Virtual-Display-Driver, MIT license, SignPath-signed).
# It installs nothing: it puts the files in tools\virtual-display\vdd-download\extracted\, which install.ps1 then uses (as administrator).
#
# Usage:  powershell -NoProfile -File .\tools\virtual-display\get-driver.ps1
#
# Security: the zip is accepted only if its SHA256 hash matches the one pinned here (the same as the project's winget manifest) and
# the driver files have a valid SignPath Foundation signature. If not, the zip is deleted and the script stops.
# For a different version pass -Url and -Sha256 (and check the origin yourself).
param(
    [string]$Url = 'https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VDD.Control.25.7.23.zip',
    [string]$Sha256 = 'A701F2272E9FCF382849B24F913C6DD07597B3B1116525F2E90182F019609154',
    [string]$Dir = (Join-Path $PSScriptRoot 'vdd-download'),
    [switch]$Force,
    [switch]$SkipSignatureCheck     # only for tests with a fake archive
)
$ErrorActionPreference = 'Stop'

$zip       = Join-Path $Dir (Split-Path $Url -Leaf)
$extracted = Join-Path $Dir 'extracted'
$needed    = @('Dependencies\devcon.exe', 'SignedDrivers\x86\VDD\MttVDD.inf')

function Test-Prepared { -not ($needed | Where-Object { -not (Test-Path (Join-Path $extracted $_)) }) }

if (-not $Force -and (Test-Prepared)) {
    Write-Host "Driver already prepared in $extracted"
    return
}

New-Item -ItemType Directory -Force $Dir | Out-Null

if ($Force -or -not (Test-Path $zip)) {
    Write-Host "Downloading $Url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $tmp = "$zip.part"
    Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing
    Move-Item $tmp $zip -Force
}
Write-Host ("Archive: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

$actual = (Get-FileHash $zip -Algorithm SHA256).Hash
if ($actual -ne $Sha256.ToUpperInvariant()) {
    Remove-Item $zip -Force
    throw "SHA256 hash differs from the expected one: the archive was deleted.`n  expected: $Sha256`n  got:      $actual"
}
Write-Host 'Hash SHA256: OK'

if (Test-Path $extracted) { Remove-Item $extracted -Recurse -Force }
Expand-Archive $zip -DestinationPath $extracted -Force
if (-not (Test-Prepared)) { throw "Archive differs from what was expected: files are missing in $extracted" }

if (-not $SkipSignatureCheck) {
    foreach ($rel in 'SignedDrivers\x86\VDD\MttVDD.dll', 'SignedDrivers\x86\VDD\mttvdd.cat') {
        $s = Get-AuthenticodeSignature (Join-Path $extracted $rel)
        if ($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'SignPath Foundation') {
            throw "Invalid signature on ${rel}: $($s.Status) / $($s.SignerCertificate.Subject)"
        }
    }
    Write-Host 'SignPath Foundation signatures: OK'
}
Write-Host "Driver ready in $extracted. Now install it with install.ps1 (PowerShell as administrator)."
