# Monitor virtuale: Virtual Display Driver

FreeExtend usa un monitor virtuale di Windows, fornito dal [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)
(driver a modalità utente IDD). Il driver non fa parte di questo repository: `get-driver.ps1` lo scarica dalla release ufficiale.

## Cosa si usa

| Voce | Valore |
|------|--------|
| Progetto | Virtual Display Driver (IDD), https://github.com/VirtualDrivers/Virtual-Display-Driver |
| Versione | 25.7.23 (pacchetto `VDD.Control.25.7.23.zip`, circa 68 MB) |
| Licenza | MIT |
| Firma | Firmato con certificato SignPath Foundation: `MttVDD.dll` e `mttvdd.cat` risultano `Valid` con `Get-AuthenticodeSignature` |
| Hash SHA256 dello zip | `A701F2272E9FCF382849B24F913C6DD07597B3B1116525F2E90182F019609154` (uguale a quello del manifest winget) |
| Test signing / Secure Boot | **Non servono** e non vanno toccati. |

Il pacchetto contiene anche un driver audio virtuale (`SignedDrivers\x86\VAD`): **non viene installato**. La cartella `x86` contiene in
realtà il driver a 64 bit (l'INF dichiara `NTamd64`). Il programma di controllo grafico `VDD Control.exe` non si usa: su un PC provato
non avviava nessuna installazione, per cui lo script fa le stesse cose in modo visibile.

## Installazione

PowerShell **come amministratore** (tasto destro > Esegui come amministratore), dalla cartella del progetto:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\virtual-display\install.ps1
```

Lo script, in ordine:

1. scarica il driver se manca (`get-driver.ps1`): accetta lo zip solo se l'hash SHA256 coincide e se le firme sono valide, altrimenti lo cancella;
2. sceglie la scheda grafica per il monitor virtuale e scrive la configurazione in `C:\VirtualDisplayDriver\vdd_settings.xml`;
3. installa il dispositivo `Root\MttVDD` con `devcon.exe` (firmato Microsoft) e stampa schermi e dispositivo risultanti.

Opzioni:

| Opzione | Effetto |
|---------|---------|
| `-Gpu "nome scheda"` | Scheda che ospita il monitor virtuale. Deve essere **la stessa che codifica il video**. Se non la indichi: l'unica scheda, oppure la NVIDIA se ce ne sono più di una, oppure te la chiede. Il nome è quello di Gestione dispositivi > Schede video. |
| `-ExtraModes 1920x1080,2560x1440` | Risoluzioni in più per il tuo tablet. Di base il driver ne offre tre, tutte 16:10 (2304x1440, 1920x1200, 2560x1600): un tablet con altre proporzioni non troverebbe "nessun modo adatto". Il tablet comunica la sua risoluzione e il programma sceglie il modo con lo stesso rapporto d'aspetto. |
| `-ConfigOnly [-OutFile file.xml]` | Scrive solo la configurazione, senza installare (non serve l'amministratore). |

Il monitor appena creato risulta "non attaccato" al desktop: non serve fare nulla, FreeExtend lo attacca da solo quando il tablet si connette
e lo stacca alla fine. Controllo manuale: `powershell -NoProfile -File tools\virtual-display\check.ps1` (verifica 2304x1440@60 con il modo a 90 Hz).

## Cambiare scheda o risoluzioni dopo l'installazione

Si può senza reinstallare, con la pipe di controllo del driver (`vdd-pipe.ps1`; accetta comandi da qualunque utente locale):

```powershell
& .\tools\virtual-display\vdd-pipe.ps1 -Command 'SETGPU "NVIDIA GeForce RTX 3050 Laptop GPU"'   # ricarica il driver
& .\tools\virtual-display\vdd-pipe.ps1 -Command 'HARDWARECURSOR false'                            # puntatore dentro l'immagine
```

Oppure si modifica `C:\VirtualDisplayDriver\vdd_settings.xml` e si manda `RELOAD_DRIVER`. Nota: a ogni ricarica cambia il numero dello
schermo (`\\.\DISPLAY58`, 59, 60...); FreeExtend lo ritrova da solo.

## Rimozione

- `remove.ps1` (PowerShell come amministratore), oppure
- Gestione dispositivi > Schede video > Virtual Display Driver > Disinstalla dispositivo.
- Se lo schermo diventa nero: avvio in modalità provvisoria, poi disinstallare il dispositivo da Gestione dispositivi.

## Sicurezza

La pipe `\\.\pipe\MTTVirtualDisplayPipe` è aperta a tutti gli utenti locali (così funziona il driver upstream): qualunque programma sul PC può
cambiarne scheda e risoluzioni. Per uno strumento personale è accettabile; su un PC condiviso va tenuto presente.
