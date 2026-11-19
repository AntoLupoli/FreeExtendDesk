# FreeExtend: finestra di controllo (GUI) e interfaccia in inglese: design

Data: 2026-11-19. Stato: da approvare.

## Intento e perche

Oggi FreeExtend si usa da console: `Start-FreeExtend.ps1` (Ctrl+C per fermare), `check.ps1`, `remove.ps1`, `FreeExtend.Host.exe gpu-status` e simili.
L'utente (Antonio, unico utente attuale, ma il progetto e pensato per essere pubblicabile su GitHub) vuole una finestra **semplice e leggera** che sostituisca la
console. Uso quotidiano descritto da lui: *"collego il tablet, apro l'app e abilito la condivisione schermo"*. Quindi la finestra si apre quando serve, **non**
resta in esecuzione in background, non parte con Windows e non reagisce da sola al cavo.

In parallelo: tutto cio che l'utente legge (finestra, README, app sul tablet, messaggi degli script e del programma sul PC) deve essere in **inglese**.

## Obiettivi

1. Una finestra Windows che mostra a colpo d'occhio:
   - driver del monitor virtuale installato (e monitor presente);
   - programma sul PC compilato e utilizzabile;
   - app sul tablet installata;
   - scheda grafica rilevata, se ha un encoder H.264 hardware utilizzabile e, su portatili con due schede, se la preferenza "Prestazioni elevate"
     per `FreeExtend.Host.exe` e impostata;
   - tablet collegato (modello) e autorizzato (debug USB).
2. Un pulsante per **avviare** e **fermare** la trasmissione, con lo stato corrente (in attesa del tablet, in trasmissione con risoluzione e fps, errore con
   messaggio comprensibile). Lo stop e pulito: il monitor virtuale viene staccato dal desktop come con Ctrl+C.
3. Accanto a ogni requisito mancante, un pulsante che lo risolve (installa il driver, imposta la preferenza grafica, installa l'app sul tablet).
4. Una schermata **Impostazioni** con "Disinstalla tutto": rimuove il driver del monitor virtuale, la voce di preferenza grafica nel registro e l'app dal tablet.
   Non cancella la cartella del progetto. *(Scelta mia, non detta dall'utente: l'utente ha chiesto di disinstallare "tutti i driver"; ho esteso a preferenza grafica e app
   perche sono gli unici altri componenti che FreeExtend mette sul sistema.)*
5. Interfaccia, README, testi dell'app Android e messaggi di `Start-FreeExtend.ps1`, `tools/*.ps1` e del programma sul PC in inglese.

## Non-obiettivi e anti-obiettivi

- Niente icona nella barra di notifica, niente avvio con Windows, niente avvio automatico alla connessione del cavo.
- Niente impostazioni di fps/risoluzione/bitrate nella finestra (restano opzioni da riga di comando).
- Niente installer MSI, niente pubblicazione, niente supporto a piu tablet.
- Anti-obiettivo: la finestra **non deve diventare pesante** (nessun framework web, nessun pacchetto NuGet di UI, nessun processo sempre acceso oltre alla
  finestra stessa) e **non deve rischiare di rompere la trasmissione** che oggi funziona.
- Anti-obiettivo: nessuna scrittura nel registro o installazione senza un click esplicito dell'utente sul pulsante relativo.
- Non si traducono i documenti interni in `docs/` e i commenti nel codice (restano in italiano). *(Scelta mia: non e interfaccia utente. Da rivedere se si pubblica.)*

## Vincoli (dal progetto esistente)

- .NET 8, `net8.0-windows`; la soluzione e `host/FreeExtend.sln`. Le classi dell'host sono `internal`.
- Installare/rimuovere il driver richiede l'**amministratore**: la finestra deve girare senza privilegi e chiedere l'elevazione (UAC) solo per quelle azioni.
- `remove.ps1` oggi richiede `devcon.exe` dentro `tools/virtual-display/vdd-download/` (scaricato da `get-driver.ps1`); se la cartella manca deve comunque funzionare.
- La preferenza grafica e legata al percorso esatto di `FreeExtend.Host.exe`: la finestra deve avviare **quell'exe** (non `dotnet ...dll`).
- Il monitor virtuale si stacca dal desktop con `DisplayController.Detach`; chiusure brusche sono coperte da `ConsoleExitGuard` e da una pulizia all'avvio successivo.
- Compatibilita schede grafiche: la finestra non e legata a una marca. Il programma sul PC supporta in codice NVIDIA/AMD/Intel (provata solo NVIDIA e, in parte,
  AMD integrata). Senza encoder H.264 hardware **non funziona** e la finestra lo dice chiaramente (nessun ripiego software).
- Per i test vale la regola gia in uso: niente screenshot con dati personali, ritagliare e cancellare.

## Approcci considerati

- **A. Progetto separato `FreeExtend.Gui` (Windows Forms, .NET 8)**, che avvia `FreeExtend.Host.exe stream` come processo figlio senza console. **Scelto dall'utente.**
  Pro: la trasmissione non viene toccata, una finestra che si blocca non ferma la codifica, il programma sul PC resta piccolo e testabile.
  Contro: un secondo eseguibile e un meccanismo di stop pulito da aggiungere.
- **B. Comando `gui` dentro `FreeExtend.Host.exe`.** Un solo programma e stop semplice, ma mescola finestra, console e cattura/codifica e rende l'host piu delicato.
- **C. Finestra in PowerShell.** Scartata: apertura lenta, console che lampeggia, chiusura pulita fragile.

## Parti del "come" gia decise

- Windows Forms (incluso in .NET 8 desktop, nessun pacchetto in piu), nuovo progetto `host/src/FreeExtend.Gui` nella soluzione esistente, con test sulla logica di stato.
- La finestra e un controllo/avvio: non cattura, non codifica. Avvia l'exe dell'host come processo figlio nascosto con output rediretto per mostrare lo stato.
- **Stop pulito**: il programma sul PC deve fermarsi e staccare il monitor quando riceve una richiesta di stop dalla finestra **e anche** se la finestra
  scompare (chiusa o in crash). *(Meccanismo a scelta del costruttore; requisito: entro pochi secondi il monitor e staccato e il processo e finito.)*
- Le azioni di installazione riusano gli script gia esistenti (`install.ps1`, `get-driver.ps1`, `remove.ps1`, `Start-FreeExtend.ps1` per app e preferenza)
  oppure i comandi dell'host (`setup-gpu`, `gpu-status`), invece di riscrivere la logica. Per le azioni da amministratore: avvio elevato (UAC) del solo script necessario.
- Il rilevamento dello stato del tablet usa `adb` (gia gestito dal progetto: cerca in PATH, ANDROID_HOME, `%LOCALAPPDATA%\Android\Sdk`). Il controllo periodico
  deve essere leggero (ogni pochi secondi, solo `adb devices`); i controlli piu costosi (app installata, driver, preferenza grafica) si fanno all'apertura e dopo ogni azione.
- La finestra trova la radice del progetto risalendo dalla cartella del proprio eseguibile fino a `Start-FreeExtend.ps1`.
- Si avvia con un comando semplice (script launcher che compila la finestra se manca, come fa `Start-FreeExtend.ps1` per l'host).
- Lingua: stringhe in inglese direttamente nel codice (nessun sistema di localizzazione). Si traducono: la finestra, `README.md`, `tools/virtual-display/README.md`, i quattro testi
  in `MainActivity.kt` (es. "Waiting for the PC...", "Reconnecting..."), i messaggi di `Start-FreeExtend.ps1`, `tools/*.ps1` e l'output/gli errori del programma sul PC.
  Le stringhe cercate dai test esistenti vanno aggiornate insieme.

## Lasciato al costruttore

Layout esatto e colori, nome dell'eseguibile e del launcher, formato del canale di stato tra finestra e programma, meccanismo di stop, come ottenere
l'elevazione UAC, come mostrare l'avanzamento di azioni lunghe (compilare l'app Android, scaricare il driver), struttura dei test, come reagisce la finestra
se `adb` non si trova (messaggio con istruzioni) e se il programma sul PC non e ancora compilato (pulsante "Build").

## Come si verifica

- Test automatici sulla logica di stato (cosa mostra la finestra per ogni combinazione di requisiti) e sull'elaborazione dell'output dell'host.
- Prova dal vivo sul sistema dell'utente (tablet collegato): stato, avvio, fermata, monitor staccato dopo lo stop, arresto della finestra a trasmissione in corso,
  azioni di installazione. La disinstallazione completa si prova solo con il consenso dell'utente, perche rimuove il driver reale.
- Le suite esistenti (`dotnet test host\FreeExtend.sln`, `gradlew testDebugUnitTest`) devono restare verdi dopo la traduzione.
