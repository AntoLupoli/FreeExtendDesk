# FreeExtend: un tablet Android come secondo monitor di Windows, via cavo USB-C

FreeExtend trasforma un tablet Android in un monitor esteso del PC Windows, collegato con un cavo USB-C e a bassa latenza.
Il PC cattura un monitor virtuale, lo codifica in H.264 con l'encoder **hardware** della scheda grafica e lo manda al tablet attraverso
`adb reverse` (un tunnel TCP sul cavo USB); l'app sul tablet lo decodifica con il decoder hardware e lo mostra a schermo intero.

> **Stato:** progetto personale, provato su **un solo sistema**: portatile Windows 11 con NVIDIA RTX 3050 + AMD integrata e Galaxy Tab S10 FE
> (Android 16). Altre schede e altri tablet dovrebbero funzionare ma non sono stati provati: vedi [Compatibilità](#compatibilità).

Misure su quel sistema (dettagli in [`docs/measurements/stage0.md`](docs/measurements/stage0.md)): dalla cattura al disegno sul tablet circa
30 ms alla mediana e 60-65 ms al 95° percentile a 2304x1440 e 60 Hz, nessun frame scartato; cavo a 218 Mbit/s contro circa 32 di picco.

## Avvio rapido

### 1. Requisiti

- **PC:** Windows 10/11 a 64 bit con una scheda grafica con **encoder H.264 hardware** (NVIDIA, AMD o Intel, vedi sotto).
- **Software sul PC:** [.NET 8 SDK](https://dotnet.microsoft.com/download), un JDK 17, e l'**Android SDK** (si ottiene installando Android Studio: serve `adb`,
  la piattaforma 35 e i build-tools).
- **Tablet:** Android 13 o successivo, opzioni sviluppatore con **debug USB** attivo, cavo USB-C dati.

### 2. Clona e installa il driver del monitor virtuale (una volta)

```powershell
git clone https://github.com/AntoLupoli/FreeExtendDesk.git
cd FreeExtendDesk
# PowerShell come amministratore:
powershell -ExecutionPolicy Bypass -File .\tools\virtual-display\install.ps1
```

Scarica il [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) (firmato, verifica hash e firma), sceglie la scheda grafica e
lo installa. Opzioni (`-Gpu`, `-ExtraModes` per altri tablet) in [`tools/virtual-display/README.md`](tools/virtual-display/README.md).

### 3. Collega il tablet e avvia

Collega il tablet e **accetta la richiesta "Consenti debug USB"** sul tablet. Poi:

```powershell
powershell -NoProfile -File .\Start-FreeExtend.ps1
```

La prima volta lo script compila il programma, **compila e installa l'app sul tablet** (`-InstallApp` la reinstalla), e su un portatile con due schede grafiche
ti chiede il permesso di impostare "Prestazioni elevate" per il solo eseguibile (vedi [Portatili con due schede](#portatili-con-due-schede-grafiche)). Poi attacca il monitor virtuale
a destra del pannello (alla risoluzione del tablet, 60 Hz), apre l'app e trasmette. Trascina le finestre verso destra. **Ctrl+C** ferma tutto e stacca il monitor.

Opzioni: `-Seconds N` (si ferma da solo), `-Csv file` (serie temporale della latenza), `-KeepDisplay` (lascia il monitor nel desktop), `-InstallApp`, `-Yes` (accetta le domande).
Il refresh è limitato a 60 Hz; il tablet ne offre fino a 90: `FreeExtend.Host.exe stream --fps 90`.

Se il tablet resta scollegato per 15 secondi il monitor viene staccato dal desktop (le finestre tornano sul pannello) e si riattacca al collegamento successivo.

## Compatibilità

| Cosa | Stato |
|---|---|
| NVIDIA con NVENC | **Provato** (RTX 3050 Laptop): consigliato, circa 4-12 ms di codifica |
| AMD con encoder H.264 | Il codice lo supporta; provato solo su una APU integrata: funziona ma con circa 30 ms fissi di ritardo di codifica |
| Intel (Quick Sync) | Il codice lo supporta, **mai provato** |
| **Solo CPU / nessuna scheda con encoder hardware** | **Non funziona.** Il programma cerca solo encoder H.264 hardware e usa la GPU per convertire i colori: non c'è un ripiego software |
| Tablet Android 13+ | Funziona con qualunque tablet o telefono con decoder H.264 hardware; **provato solo** su un Galaxy Tab S10 FE |
| Proporzioni diverse da 16:10 | Aggiungi la risoluzione con `install.ps1 -ExtraModes LARGHEZZAxALTEZZA` (il driver offre di base tre modi 16:10) |
| Più tablet insieme, audio, touch/penna | Non supportati |

### Portatili con due schede grafiche

Il monitor virtuale e la codifica devono stare **sulla stessa scheda**. Su un portatile con NVIDIA + scheda integrata, Windows attiva l'encoder NVIDIA solo
se esegue il programma sulla scheda NVIDIA: serve "Prestazioni elevate" per `FreeExtend.Host.exe` (Impostazioni > Sistema > Schermo > Grafica). `Start-FreeExtend.ps1`
lo propone da solo; a mano: `FreeExtend.Host.exe setup-gpu` (si annulla con `setup-gpu --remove`; stato: `gpu-status`). La preferenza è legata al **percorso esatto**
dell'eseguibile: va rifatta se sposti la cartella. Se manca, il programma lo dice (`ERRORE: Nessun encoder H.264 hardware utilizzabile...`) e si ferma.
Lancia sempre `Start-FreeExtend.ps1` o l'`.exe`, non `dotnet FreeExtend.Host.dll`.

## Se qualcosa non va

- "Il tablet non ha autorizzato il PC": sul tablet accetta "Consenti debug USB".
- Schermo nero sul tablet con l'app aperta: chiudi e riapri `Start-FreeExtend.ps1`; l'app si riconnette da sola.
- Monitor rimasto nel desktop dopo un arresto brusco: si stacca all'avvio successivo; a mano: `FreeExtend.Host.exe display-test detach`, oppure
  Impostazioni > Schermo > "Scollega questo schermo".
- Verificare il monitor virtuale: `powershell -NoProfile -File .\tools\virtual-display\check.ps1`.
- Rimuovere il driver: `tools/virtual-display/remove.ps1` (amministratore).

## Sicurezza e privacy

- L'host ascolta solo su `127.0.0.1:27183` (raggiunto dal tablet via `adb reverse`), **senza autenticazione**: un altro programma locale potrebbe connettersi e vedere il monitor virtuale.
- La pipe di controllo del driver è aperta a tutti gli utenti locali (come nel driver originale).
- Il debug USB dà al PC molto controllo sul tablet: attivalo solo su PC fidati.
- `setup-gpu` scrive una sola voce nel registro dell'utente (`HKCU\Software\Microsoft\DirectX\UserGpuPreferences`).

## Sviluppo

```powershell
dotnet test host\FreeExtend.sln                       # test del programma Windows e del protocollo
cd android; .\gradlew.bat testDebugUnitTest           # test dell'app Android
```

Documentazione: [`docs/superpowers/specs/`](docs/superpowers/specs/) (design), [`docs/superpowers/plans/`](docs/superpowers/plans/) (piano e registro delle decisioni),
[`docs/device-profile.md`](docs/device-profile.md) (hardware e scelte dell'encoder), [`docs/measurements/stage0.md`](docs/measurements/stage0.md) (misure).

## Licenza e componenti di terze parti

Questo progetto è sotto licenza **MIT** ([`LICENSE`](LICENSE)). Usa, senza ridistribuirli nel repository (si scaricano alla compilazione o all'installazione):
[Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (MIT), System.Drawing.Common (MIT), il Gradle Wrapper (Apache-2.0), JUnit (EPL-2.0, solo test) e il
[Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) (MIT, firmato da SignPath Foundation).
