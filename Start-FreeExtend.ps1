# Avvia FreeExtend: usa il tablet (collegato con il cavo USB-C) come secondo monitor di questo PC.
#
# Uso:   powershell -NoProfile -File .\Start-FreeExtend.ps1            (si ferma con Ctrl+C)
#        powershell -NoProfile -File .\Start-FreeExtend.ps1 -KeepDisplay   (lascia il monitor virtuale nel desktop alla fine)
#        powershell -NoProfile -File .\Start-FreeExtend.ps1 -InstallApp    (ricompila e reinstalla l'app sul tablet)
#
# Cosa fa: controlla il cavo e l'autorizzazione del debug USB, installa l'app sul tablet se manca, la apre e lancia il programma sul PC, che
# attacca il monitor virtuale, trasmette lo schermo e a fine sessione lo stacca. Requisiti una tantum: vedi README.md.
param([switch]$KeepDisplay, [switch]$InstallApp, [switch]$Yes, [int]$Seconds = 0, [string]$Csv = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
. "$root\tools\env.ps1" | Out-Null

$exe = Join-Path $root 'host\src\FreeExtend.Host\bin\Debug\net8.0-windows\FreeExtend.Host.exe'
if (-not (Test-Path $exe)) {
    Write-Host 'Programma non compilato: lo compilo...'
    dotnet build "$root\host\src\FreeExtend.Host" -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione non riuscita.' }
}

# Portatile con due schede grafiche: l'encoder NVIDIA si attiva solo se Windows esegue il programma sulla scheda NVIDIA ("Prestazioni
# elevate" per questo eseguibile). Si chiede il permesso prima di scrivere quella preferenza (e solo per questo eseguibile).
$gpuCount = @(Get-CimInstance Win32_VideoController |
    Where-Object { $_.Name -and $_.Name -notmatch 'Microsoft Basic|Remote|Virtual Display|Parsec|Meta Virtual|Mirage' }).Count
if ($gpuCount -ge 2) {
    & $exe gpu-status | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Questo PC ha $gpuCount schede grafiche e per questo eseguibile non e impostato 'Prestazioni elevate'."
        $answer = if ($Yes) { 's' } else { Read-Host "Lo imposto solo per `"$exe`" (come Impostazioni > Sistema > Schermo > Grafica)? [S/n]" }
        if ($answer -match '^[nN]') { Write-Host "Va bene. Se la pipeline non parte, lancia: & `"$exe`" setup-gpu" }
        else { & $exe setup-gpu }
    }
}

# Il tablet deve essere collegato e aver autorizzato il PC.
$devices = (& adb devices) -join "`n"
if ($devices -match 'unauthorized') { throw "Il tablet non ha autorizzato questo PC: sul tablet accetta 'Consenti debug USB'." }
if ($devices -notmatch '\tdevice') { throw 'Nessun tablet collegato: controlla il cavo USB-C e che il debug USB sia attivo.' }

# Installa l'app sul tablet se manca (o se si chiede -InstallApp): usa l'APK gia compilato, altrimenti lo compila.
$package = 'it.freeextend.display'
$installed = [bool]((& adb shell pm list packages $package) -match "package:$([regex]::Escape($package))\s*$")
if ($InstallApp -or -not $installed) {
    $apk = Join-Path $root 'android\app\build\outputs\apk\debug\app-debug.apk'
    if ($InstallApp -or -not (Test-Path $apk)) {
        Write-Host 'Compilo l''app Android (Gradle)...'
        $localProps = Join-Path $root 'android\local.properties'
        if (-not (Test-Path $localProps)) {
            # local.properties non e nel repository: indica a Gradle dove sta l'Android SDK di questo PC.
            $sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
            if (-not (Test-Path $sdk)) { throw "Android SDK non trovato in ${sdk}: installalo o crea android\local.properties con sdk.dir=..." }
            Set-Content $localProps ('sdk.dir=' + ($sdk -replace '\\', '/')) -Encoding ascii
        }
        Push-Location (Join-Path $root 'android')
        try { & .\gradlew.bat assembleDebug --console=plain -q } finally { Pop-Location }
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $apk)) { throw 'Compilazione dell''app Android non riuscita (serve un JDK 17 e l''Android SDK).' }
    }
    Write-Host "Installo l'app sul tablet ($package)..."
    $out = (& adb install -r $apk) -join "`n"
    if ($out -notmatch 'Success') { throw "Installazione sul tablet non riuscita:`n$out" }
}

# Apre l'app sul tablet (si connette da sola appena il programma sul PC e pronto).
& adb shell am start -n it.freeextend.display/.MainActivity | Out-Null

$argsList = @('stream')
if ($KeepDisplay) { $argsList += '--keep-display' }
if ($Seconds -gt 0) { $argsList += @('--seconds', $Seconds) }
if ($Csv) { $argsList += @('--csv', $Csv) }
& $exe @argsList
exit $LASTEXITCODE
