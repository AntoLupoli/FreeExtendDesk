# Profilo del tablet e dell'ambiente (misurato il 2026-11-18)

## Tablet (via `adb`, seriale <seriale>)

| Voce | Valore |
|------|--------|
| Modello | SM-X520 (Galaxy Tab S10 FE) |
| Android | 16 (SDK 36) |
| SoC | s5e8855 (Exynos 1580), piattaforma `erd8855` |
| Pannello nativo | 1440 x 2304 in verticale = **2304 x 1440 in orizzontale** |
| Densità | 280 dpi (circa 249 dpi reali) |
| Refresh supportati | **90, 60, 30 Hz** (modo attivo: 90 Hz) |
| HDR | tipi 2 e 3 supportati (non usato) |

La stima della spec (2304x1440 a 90 Hz) è confermata. L'app va fissata in orizzontale e deve mandare all'host 2304x1440 con il refresh scelto.

Decoder H.264: `dumpsys media.player` elenca solo `c2.android.avc.decoder` (software). L'elenco dei codec hardware si verifica da dentro l'app con `MediaCodecList` (T8), prima di fidarsi della latenza.

## PC

- Windows 11 Home, schermo principale 1707x1067 (scalato).
- GPU: NVIDIA RTX 3050 Laptop (driver 32.0.15.8180) + AMD Radeon integrata.
- Strumenti: JDK 17, Node, Python, Git, `winget`; **.NET: solo runtime 8.0.15, nessun SDK** (da installare, T1 step 2).
- Android SDK in `%LOCALAPPDATA%\Android\Sdk`: platform 34 e 35, build-tools 34.0.0 e 36.0.0, platform-tools con `adb` 37.0.1 (`. .\tools\env.ps1` lo mette nel PATH). Nessuna platform 36 (non serve: compile/target 35).
- Gradle: nessun comando `gradle` nel PATH, ma in `%USERPROFILE%\.gradle` ci sono la distribuzione wrapper `gradle-9.1.0-bin` e plugin Android Gradle 9.0.1 già in cache. Il progetto Android (T4) usa wrapper Gradle 9.1.0 e AGP 9.0.1, riusando la cache.

## Monitor virtuale (verificato in T2)

- Driver: Virtual Display Driver 25.7.23 (SignPath Foundation), dispositivo `Root\MttVDD`, monitor `DISPLAY\MTT1337` ("VDD by MTT"), su Windows `\\.\DISPLAY58`.
- **Scheda che lo ospita: NVIDIA RTX 3050 Laptop** (log del driver: `ASSIGNED GPU: ... (LUID: 67966-0)`), come richiesto in `vdd_settings.xml`.
- **Il pannello del portatile è sulla scheda AMD** (`\\.\DISPLAY1`, 2560x1600 a 120 Hz). Il desktop ha quindi due adattatori: la cattura del monitor virtuale e NVENC stanno su NVIDIA, la composizione di Windows parte da AMD. Il costo di questa copia tra schede si misura in T9.
- Stato: attaccato al desktop in modalità Estendi, `2304x1440@60`, posizione (2560,0), scala di Windows 125%. Modi offerti: 1920x1200, 2304x1440 e 2560x1600, tutti a 60 e 90 Hz.
- Il driver espone una named pipe di controllo (`[PIPE] Pipe created` nel log): da esaminare in T10 per aggiungere/togliere il monitor senza toccare i settaggi di Windows.
- Verifica ripetibile: `powershell -NoProfile -File tools\virtual-display\check.ps1`.

## Encoder (verificato in T6)

- **Encoder usato: `NVIDIA H.264 Encoder MFT` (NVENC) tramite Media Foundation**, con il monitor virtuale sulla scheda NVIDIA (`SETGPU "NVIDIA GeForce RTX 3050 Laptop GPU"` dalla pipe del driver). Accetta ingressi NV12, Y420O e ARGB32; si usa NV12 (conversione BGRA->NV12 sul video processor D3D11: circa 3,6 ms di attesa GPU, 0,7 ms di tempo CPU).
- **Preferenza grafica richiesta:** senza, l'MFT NVIDIA non si attiva (`E_UNEXPECTED`) su questo portatile ibrido. Voce scritta in `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` per `...\host\src\FreeExtend.Host\bin\Debug\net8.0-windows\FreeExtend.Host.exe` con valore `GpuPreference=2;` (Prestazioni elevate), con il permesso dell'utente. Si rimuove da Impostazioni > Sistema > Schermo > Grafica. Con la preferenza attiva gli MFT AMD non si attivano piu (`E_OUTOFMEMORY`) per quel processo: e voluto. Va ripetuta per ogni nuovo percorso dell'exe (es. build Release).
- **L'encoder AMD** (`AMDh264Encoder`, usato senza preferenza) funziona ma ha circa 30 ms di latenza e ~27 fps massimi in qualunque configurazione provata (qualita, risoluzione, fps): scartato.
- **Misure (frame sintetici, solo encoder):** 226 fps a 2304x1440 con 4,4 ms di latenza al massimo ritmo; 10,5 ms a 60 fps offerti e 10,9 ms a 30 fps (la GPU scende di clock quando il carico e leggero). Catena reale cattura+codifica con la finestra animata: 11,9 ms medi, **7,4 ms mediani**, p95 35,8 ms (l'animazione di prova arriva a circa 33 fps).
- **Lezione importante (costata ore):** una `AcquireNextFrame` con attesa lunga tiene il lock del device D3D11 condiviso con l'encoder MF. Con la cattura in parallelo l'encoder scendeva a 5-15 fps e 40-190 ms di latenza, anche senza copiare frame. Soluzione adottata: interrogare con timeout 0 + `Sleep(1)` (timer di sistema a 1 ms con `timeBeginPeriod(1)`). Se in futuro si separa cattura ed encoder su due device, il problema sparisce alla radice.
- Parametri `ICodecAPI` su NVIDIA: LowLatencyMode, CBR, MeanBitRate, GopSize accettati; CommonLowLatency, CommonRealTime e DefaultBPictureCount=0 rifiutati (`E_INVALIDARG`), ma l'uscita e comunque in ordine (nessun B-frame osservato) e un keyframe a richiesta funziona (`ForceKeyFrame`).

## Decisioni derivate

- Dispositivo di destinazione: `2304x1440` landscape, refresh 60 Hz di partenza, 90 Hz in T12.
- `adb devices` mostra il tablet come `device` (già autorizzato), il PC è fidato.
