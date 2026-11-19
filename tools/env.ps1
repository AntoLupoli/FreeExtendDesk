# Uso: . .\tools\env.ps1
# Rende disponibile adb (Android SDK platform-tools) nella sessione PowerShell corrente. Lo cerca, in ordine:
#   1. gia nel PATH; 2. ANDROID_HOME; 3. ANDROID_SDK_ROOT; 4. %LOCALAPPDATA%\Android\Sdk (posizione di Android Studio).
$adb = (Get-Command adb -ErrorAction SilentlyContinue).Source
if (-not $adb) {
    $roots = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk')) | Where-Object { $_ }
    foreach ($r in $roots) {
        $candidate = Join-Path $r 'platform-tools\adb.exe'
        if (Test-Path $candidate) { $adb = $candidate; break }
    }
}
if (-not $adb) {
    Write-Error "adb non trovato: installa l'Android SDK (platform-tools) o Android Studio, oppure imposta ANDROID_HOME."
    return
}
$dir = Split-Path $adb
if (($env:PATH -split ';') -notcontains $dir) { $env:PATH = "$dir;$env:PATH" }
Write-Host "adb pronto: $dir"
