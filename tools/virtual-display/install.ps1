# Installs the Virtual Display Driver (the video driver only) with devcon, without VDD Control, and writes its configuration.
# Usage (PowerShell as administrator):
#   powershell -ExecutionPolicy Bypass -File .\tools\virtual-display\install.ps1
#   ... -Gpu "NVIDIA GeForce RTX 3050 Laptop GPU"   graphics card that hosts the virtual monitor (the same as the encoder); usually chosen automatically
#   ... -ExtraModes 1920x1080,2560x1440             extra resolutions for your tablet (the driver offers three by default, all 16:10)
#   ... -ConfigOnly [-OutFile file.xml]             only writes the configuration, without installing (no administrator needed)
#
# What it does, in order:
#   1. downloads the driver if missing (get-driver.ps1: SHA256 hash and SignPath signature verified);
#   2. picks the graphics card and writes the configuration to C:\VirtualDisplayDriver\vdd_settings.xml;
#   3. installs the Root\MttVDD device with devcon.exe (Microsoft-signed);
#   4. prints the resulting screens and device.
# It does not touch test signing, Secure Boot or the audio driver. To remove: remove.ps1.
param(
    [string]$Gpu = '',
    [string[]]$ExtraModes = @(),
    [switch]$ConfigOnly,
    [string]$OutFile = ''
)
$ErrorActionPreference = 'Stop'

$template = Join-Path $PSScriptRoot 'vdd_settings.freeextend.xml'
if (-not (Test-Path $template)) { throw "Missing file: $template" }

# --- graphics card ------------------------------------------------------------------------------
function Get-GraphicsAdapters {
    @(Get-CimInstance Win32_VideoController |
        Where-Object { $_.Name -and $_.Name -notmatch 'Microsoft Basic|Remote|Virtual Display|Parsec|Meta Virtual|Mirage' } |
        ForEach-Object { $_.Name } | Select-Object -Unique)
}

function Select-Gpu([string]$wanted, [string[]]$adapters, [bool]$interactive) {
    if ($wanted) { return $wanted }
    if ($adapters.Count -eq 0) { throw 'No graphics card found: name the one to use with -Gpu "name".' }
    if ($adapters.Count -eq 1) { return $adapters[0] }
    $nvidia = @($adapters | Where-Object { $_ -match 'NVIDIA' })
    if ($nvidia.Count -ge 1) { return $nvidia[0] }          # NVENC is the fastest encoder tested; see the README (laptops with two cards)
    if (-not $interactive) { return $adapters[0] }
    Write-Host 'Several graphics cards: which one should run the virtual monitor (and the encoding)?'
    for ($i = 0; $i -lt $adapters.Count; $i++) { Write-Host ("  {0}) {1}" -f ($i + 1), $adapters[$i]) }
    $n = [int](Read-Host 'Number')
    if ($n -lt 1 -or $n -gt $adapters.Count) { throw 'Invalid choice.' }
    return $adapters[$n - 1]
}

$adapters = Get-GraphicsAdapters
$gpuName = Select-Gpu $Gpu $adapters (-not $ConfigOnly)
Write-Host "Graphics card for the virtual monitor: $gpuName"

# --- configuration ------------------------------------------------------------------------------
[xml]$xml = Get-Content $template -Raw
$xml.vdd_settings.gpu.friendlyname = [string]$gpuName
foreach ($m in $ExtraModes) {
    if ($m -notmatch '^(\d{3,5})x(\d{3,5})$') { throw "Invalid resolution '$m': use the format WIDTHxHEIGHT, for example 1920x1080." }
    $r = $xml.CreateElement('resolution')
    foreach ($pair in @(@('width', $Matches[1]), @('height', $Matches[2]), @('refresh_rate', '60'))) {
        $e = $xml.CreateElement($pair[0]); $e.InnerText = $pair[1]; [void]$r.AppendChild($e)
    }
    [void]$xml.vdd_settings.resolutions.AppendChild($r)
}

if ($ConfigOnly) {
    $target = if ($OutFile) { $OutFile } else { Join-Path $PSScriptRoot 'vdd_settings.generated.xml' }
    $xml.Save($target)
    Write-Host "Configuration written to $target (nothing installed)."
    return
}

# --- installation (needs administrator) ----------------------------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'PowerShell as administrator is required (right-click > Run as administrator).' }

& (Join-Path $PSScriptRoot 'get-driver.ps1')

$root   = Join-Path $PSScriptRoot 'vdd-download\extracted'
$drvDir = Join-Path $root 'SignedDrivers\x86\VDD'     # (the folder is called "x86" but holds the x64 driver, INF NTamd64)
$devcon = Join-Path $root 'Dependencies\devcon.exe'
$inf    = Join-Path $drvDir 'MttVDD.inf'
foreach ($p in $devcon, $inf) { if (-not (Test-Path $p)) { throw "Missing file: $p" } }

Write-Host '1) Checking signatures...'
foreach ($f in 'MttVDD.dll', 'mttvdd.cat') {
    $s = Get-AuthenticodeSignature (Join-Path $drvDir $f)
    if ($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'SignPath Foundation') {
        throw "Invalid signature on ${f}: $($s.Status) / $($s.SignerCertificate.Subject)"
    }
    Write-Host "   $f OK ($($s.SignerCertificate.Subject -replace ',.*$',''))"
}

Write-Host '2) Configuration in C:\VirtualDisplayDriver ...'
New-Item -ItemType Directory -Force 'C:\VirtualDisplayDriver' | Out-Null
$xml.Save('C:\VirtualDisplayDriver\vdd_settings.xml')

Write-Host '3) Installing the Root\MttVDD device ...'
& $devcon install $inf 'Root\MttVDD'
if ($LASTEXITCODE -ne 0) { throw "devcon returned code $LASTEXITCODE (see the message above)" }

Write-Host '4) Result:'
Start-Sleep -Seconds 3
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Screen]::AllScreens | ForEach-Object { "   $($_.DeviceName) $($_.Bounds.Width)x$($_.Bounds.Height) primary=$($_.Primary)" }
Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | Select-Object Status, FriendlyName | Format-Table -AutoSize | Out-String | Write-Host
Write-Host 'The new monitor shows up as "not attached": FreeExtend attaches it by itself when the tablet connects.'
