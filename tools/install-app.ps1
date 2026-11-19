# Installs the FreeExtend app on the connected Android tablet (builds the APK first if it is missing).
#
# Usage: powershell -NoProfile -File .\tools\install-app.ps1            (skips if the app is already installed)
#        powershell -NoProfile -File .\tools\install-app.ps1 -Force     (rebuilds and reinstalls)
#
# Needs: adb (Android SDK platform-tools), a tablet with USB debugging authorized, and for the build a JDK 17 and the Android SDK.
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot\env.ps1" | Out-Null

$devices = (& adb devices) -join "`n"
if ($devices -match 'unauthorized') { throw "The tablet has not authorized this PC: on the tablet, accept 'Allow USB debugging'." }
if ($devices -notmatch '\tdevice') { throw 'No tablet connected: check the USB-C cable and that USB debugging is on.' }

$package = 'it.freeextend.display'
$installed = [bool]((& adb shell pm list packages $package) -match "package:$([regex]::Escape($package))\s*$")
if ($installed -and -not $Force) { Write-Host 'App already installed.'; return }

$apk = Join-Path $root 'android\app\build\outputs\apk\debug\app-debug.apk'
if ($Force -or -not (Test-Path $apk)) {
    Write-Host 'Building the Android app (Gradle)...'
    $localProps = Join-Path $root 'android\local.properties'
    if (-not (Test-Path $localProps)) {
        # local.properties is not in the repository: tell Gradle where this PC's Android SDK is.
        $sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
        if (-not (Test-Path $sdk)) { throw "Android SDK not found in ${sdk}: install it or create android\local.properties with sdk.dir=..." }
        Set-Content $localProps ('sdk.dir=' + $sdk.Replace('\', '/')) -Encoding ascii
    }
    Push-Location (Join-Path $root 'android')
    try { & .\gradlew.bat assembleDebug --console=plain -q } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $apk)) { throw 'Building the Android app failed (needs a JDK 17 and the Android SDK).' }
}
Write-Host "Installing the app on the tablet ($package)..."
$out = (& adb install -r $apk) -join "`n"
if ($out -notmatch 'Success') { throw "Installing on the tablet failed:`n$out" }
Write-Host 'App installed.'
