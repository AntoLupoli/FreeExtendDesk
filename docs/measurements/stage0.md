# Misure della tappa 0

Hardware: portatile con RTX 3050 Laptop + AMD Radeon integrata, tablet Galaxy Tab S10 FE (SM-X520, Android 16, Exynos 1580), cavo USB-C, `adb reverse`. Monitor virtuale 2304x1440 a 60 Hz sulla NVIDIA. Sezioni aggiunte man mano che i task le producono; il confronto con i criteri della spec e in T9.

## Cavo USB (Task 7/8, `FreeExtend.Host.exe tunnel-test --seconds 15`)

- **Throughput sostenuto: 26,0 MiB/s (218 Mbit/s)** con Ping da 64 KiB, 8 in volo, 6239 messaggi, nessuna perdita.
- Margine: il bitrate dell'encoder arriva a ~10-13 Mbit/s nei picchi di prova (CBR impostato a 40 Mbit/s): oltre 5 volte sotto il tetto, quindi il criterio "il tunnel sostiene il doppio del bitrate" della spec e rispettato con ampio margine.
- RTT con coda (pacchetti da 64 KiB): minimo 3,5 ms, p50 18,4 ms, p95 20,5 ms. Il p50 misura soprattutto la coda dei pacchetti grandi in volo, non il ritardo del cavo: l'RTT vero si misura in T9 con Ping piccoli.

## Decoder sul tablet (Task 8)

- Decoder scelto: `c2.exynos.h264.decoder` (hardware). Alternative presenti solo software (`c2.android.avc.decoder`, `OMX.google.h264.decoder`).
- Supporta 2304x1440 (larghezze e altezze 32-4096) e `FEATURE_LowLatency`.
- **Attenzione a `KEY_OPERATING_RATE`:** con 32767 fps `MediaCodec.start()` falliva con `ERROR_INSUFFICIENT_RESOURCE` (1100) e l'app andava in crash; con 120 fps funziona.

## Catena completa PC -> tablet (Task 8, finestra animata di prova sul monitor virtuale)

- Il PC codifica 23-39 frame/s (la finestra di prova in PowerShell non arriva a 60 fps) e il tablet ne disegna **lo stesso numero** secondo i `Rendered` ricevuti: nessuna perdita, coda sempre a 0, nessun frame scartato.
- Bitrate: 5-6 Mbit/s a regime (13 Mbit/s sul primo keyframe).
- L'immagine sul tablet corrisponde al monitor virtuale: contatore in millisecondi, barra in movimento, testo piccolo nitido a 2304x1440 (verificato con uno screenshot, poi eliminato perche mostrava anche la schermata personale del tablet).

## Latenza della pipeline (Task 9, `tools/stage0-measure.ps1`)

Definizione: tempo dalla cattura del frame sul PC al suo disegno sul tablet, tutto sull'orologio del PC come `(ricezione dell'ack Rendered - cattura) - RTT/2` (RTT = mediana degli ultimi 16 Ping/Pong da 250 ms, in coda con il video). **Non comprende** l'attesa della cattura (circa 4 ms in media a 120 Hz di composizione) e la scansione del pannello del tablet (circa 5-8 ms), ne il percorso di ingresso (mouse -> applicazione).

| Sorgente (60 s ciascuna, monitor virtuale 2304x1440@60 sulla NVIDIA, H.264 CBR 40 Mbit/s) | Frame disegnati | Pipeline p50 | p95 | max | RTT mediano | Codifica media (cattura->pacchetto) | Scartati |
|---|---|---|---|---|---|---|---|
| Finestra PowerShell/GDI+ (circa 33 fps, 5-6 Mbit/s) - prova di 20 s | 635 | 29,3 ms | 60,7 ms | 100,8 ms | 7,8 ms | 13,2 ms | 0 |
| Canvas nel browser (35-47 fps, 25-32 Mbit/s) | 2506 | 32,5 ms | 63,1 ms | 153,3 ms | 10,6 ms | 16,0 ms | 0 |

Serie temporale della seconda misura: `docs/measurements/stage0-browser.csv`.

### Confronto con i criteri della spec

| Criterio | Esito |
|---|---|
| Latenza ordine di 30-60 ms | **Mediana 29-33 ms** nella pipeline misurata. Stima end-to-end aggiungendo cio che non si misura (attesa cattura ~4 ms + scansione tablet ~5-8 ms): **circa 40-45 ms alla mediana**, p95 circa 75 ms. Nell'intervallo alla mediana; il p95 lo supera di poco. |
| Meglio del wireless attuale | Atteso (il wireless dell'utente supera i 100 ms), **non confrontato direttamente**: serve il controllo incrociato dal vivo (telefono a rallentatore con un cronometro in ms), che non e stato eseguito. |
| Tunnel: < ~10 ms aggiunti | RTT mediano 8-11 ms con il video in coda (andata circa 4-5 ms): al limite, ma comprende l'attesa dietro ai pacchetti video e il risveglio del thread sul tablet. |
| Tunnel: sostiene il doppio del bitrate | 218 Mbit/s sostenuti contro un bitrate di picco di circa 32 Mbit/s: **ampiamente rispettato**. |
| Codifica < ~5 ms | **Non rispettato nella catena reale**: 13-16 ms medi (4,4 ms solo con frame sintetici al massimo ritmo). La GPU scende di clock con carico leggero. E la parte piu grande del budget. |
| Frame persi / latenza che cresce | **Nessun frame scartato, coda sempre a 0**, nessuna crescita nel tempo in 60 s. |

### Dove va il tempo (mediana circa 30 ms)

- Codifica (cattura -> pacchetto pronto): 13-16 ms.
- Il resto, circa 16 ms: coda di invio, cavo (circa 4-5 ms andata), lettura sul tablet, decodifica hardware, rilascio sulla superficie e ack.
- Code lunghe (p95 60 ms, max 100-150 ms): picchi sporadici di codifica o di scheduling; da indagare in T11/T12 se danno fastidio.

### Possibili miglioramenti (non eseguiti)

1. Tenere la GPU a clock alto durante lo streaming (la codifica sintetica passa da 10 a 4 ms con carico continuo).
2. Separare cattura ed encoder su due device D3D11 con texture condivisa (elimina la contesa sul lock).
3. Ridurre il lavoro per frame: ingresso ARGB32 diretto all'MFT al posto della conversione NV12 (risparmio atteso circa 1-4 ms).
4. Misurare l'attesa di scansione del tablet con un controllo incrociato dal vivo.

## Tentativo a 90 Hz (Task 12, `tools/stage0-measure.ps1 -Fps 60|90`, canvas nel browser, 40 s ciascuno)

L'app dichiara all'host il refresh **massimo** del tablet (90 Hz) e chiede al sistema di far girare lo schermo al ritmo del flusso (`Surface.setFrameRate`); l'host sceglie il modo del monitor virtuale con il tetto `--fps` (60 di default).

| Tetto | Modo scelto | Frame disegnati | Pipeline p50 | p95 | max | RTT mediano | Codifica media | Scartati |
|---|---|---|---|---|---|---|---|---|
| `--fps 60` | 2304x1440@60 | 1650 | 31,0 ms | 66,7 ms | 168,7 ms | 13,1 ms | 16,0 ms | 0 |
| `--fps 90` | 2304x1440@90 | 1805 | 31,4 ms | 60,9 ms | 152,6 ms | 6,3 ms | 14,2 ms | 0 |

Serie temporali: `stage0-browser-60.csv`, `stage0-browser-90.csv` (la prima riga utile e dopo le intestazioni).

- Criterio della spec ("frame scartati < 1% e latenza p95 non peggiore di quella a 60 Hz"): **rispettato** (0 scartati; p95 60,9 contro 66,7 ms, nel rumore della misura).
- **Limite della prova:** la sorgente (canvas del browser attraverso il compositore di Windows) arriva in media a 41 fps a 60 Hz e a 45 fps a 90 Hz, con picchi a 61 fps: il decoder del tablet non e stato messo alla prova con un vero flusso a 90 fps continui. I frame scartati contati sono solo quelli dell'encoder; i frame persi dal decoder del tablet non sono contati (se ce ne fossero, l'app chiederebbe un keyframe e la latenza lo mostrerebbe).
- **Decisione:** il valore predefinito resta **60 Hz** (l'utente ha indicato 60 fps come massimo); `--fps 90` e disponibile come opzione e non peggiora i risultati misurati.
