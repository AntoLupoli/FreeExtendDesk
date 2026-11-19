# Usage: . .\tools\env.ps1
# Makes adb (Android SDK platform-tools) available in the current PowerShell session. It looks for it, in order:
#   1. already in PATH; 2. ANDROID_HOME; 3. ANDROID_SDK_ROOT; 4. %LOCALAPPDATA%\Android\Sdk (Android Studio's default location).
$adb = (Get-Command adb -ErrorAction SilentlyContinue).Source
if (-not $adb) {
    $roots = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk')) | Where-Object { $_ }
    foreach ($r in $roots) {
        $candidate = Join-Path $r 'platform-tools\adb.exe'
        if (Test-Path $candidate) { $adb = $candidate; break }
    }
}
if (-not $adb) {
    Write-Error "adb not found: install the Android SDK (platform-tools) or Android Studio, or set ANDROID_HOME."
    return
}
$dir = Split-Path $adb
if (($env:PATH -split ';') -notcontains $dir) { $env:PATH = "$dir;$env:PATH" }
Write-Host "adb ready: $dir"
