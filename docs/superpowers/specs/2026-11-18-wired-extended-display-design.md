# Schermo esteso via cavo USB-C: PC Windows -> Galaxy Tab S10 FE

Data: 2026-11-18. Stato: bozza da approvare. Cartella di progetto: la radice di questo repository.

## Intento e perché

L'utente usa il tablet Samsung Galaxy Tab S10 FE come schermo secondario del suo PC Windows personale. Oggi usa la funzione wireless di Windows, che ha una latenza fastidiosa. Spacedesk, provato come alternativa, "non funziona molto bene".
Vuole un'app propria (una parte sul PC, una sul tablet) che faccia lo stesso lavoro di Spacedesk passando dal cavo USB-C, con latenza bassa. Non sappiamo ancora in che cosa Spacedesk fallisca di preciso (ritardo, qualità del testo, disconnessioni): il design assume che sia soprattutto il ritardo, come detto nella prima richiesta.

Uso tipico: schermo secondario generico (finestre, testo, documenti, qualche video), per una persona sola, un PC e un tablet.

## Obiettivi

- Il tablet compare in Windows come un vero secondo monitor, che si può estendere e posizionare in Impostazioni > Schermo.
- Collegamento solo via cavo USB-C. Niente Wi-Fi.
- Latenza percepita il più bassa possibile. Obiettivo da misurare: ordine di 30-60 ms dal movimento del mouse al pixel sul tablet. Il numero è un'ipotesi, non una promessa, e va verificato con una misura reale (vedi Tappe).
- Testo nitido: risoluzione nativa del tablet (circa 2304x1440 a 90 Hz, da verificare sul dispositivo) o una scelta sensata vicina.
- Avvio semplice: collego il cavo, lancio il programma sul PC, il tablet mostra lo schermo.

## Non obiettivi

- Wireless, internet, più tablet o più PC, altri sistemi operativi.
- Audio.
- Pubblicazione su Play Store, sicurezza da prodotto pubblico (uso personale su cavo).
- Contenuti protetti da DRM, che Windows può oscurare in cattura.
- All'inizio: touch e S Pen verso Windows (è l'ultima tappa, opzionale).

## Anti-obiettivi (sarebbe un fallimento anche se "funziona")

- Latenza che cresce nel tempo per code di frame accumulate.
- Testo sfocato o compresso male.
- Procedura di avvio lunga o fragile ogni volta (abilitare impostazioni a mano, riavvii, ecc.).
- Rendere il PC instabile con un driver fatto male.

## Vincoli noti (dalla ricognizione sul PC)

- Windows 11 Home.
- GPU doppia da portatile: NVIDIA RTX 3050 Laptop (con encoder NVENC) e AMD Radeon integrata. Quale scheda ospita il monitor virtuale e quale fa la codifica incide sulla latenza (copie tra schede diverse costano tempo). È un punto da verificare presto.
- Sul PC ci sono: JDK 17, .NET, Node, Python, Git, Android SDK in `%LOCALAPPDATA%\Android\Sdk`. `adb` esiste ma non è nel PATH. Non ci sono CMake né un compilatore C++.
- Il tablet deve avere "Debug USB" attivo e il PC autorizzato, perché il tunnel passa da `adb`.

## Approcci considerati

- **A. Tutto nostro (scelto dall'utente).** Programma host sul PC (cattura, codifica, invio) + app Android (decodifica, disegno) + driver di monitor virtuale open source già pronto. Più lavoro, ma controllo completo su ritardo, frame persi e qualità.
- **B. Sunshine + Moonlight su USB.** Due programmi open source per il game streaming, latenza ottima. Scartato come soluzione perché non è un'app propria, il monitor esteso è poco rifinito e il touch è limitato. Resta utile come riferimento di latenza (facoltativo).
- **C. Spacedesk forzato su USB tethering.** Scartato: il collo di bottiglia è il suo codec, quindi il guadagno sarebbe poco.

## Decisioni prese

Dell'utente:
- Si costruisce l'approccio A.

Mie scelte (da correggere se non vanno bene):
- **Host PC in C# (.NET).** Più veloce da scrivere, e le librerie per DXGI/Direct3D sono mature. Se la misura mostra che il linguaggio è un limite, si rivaluta. Non c'è un compilatore C++ installato.
- **App Android in Kotlin**, decodifica con `MediaCodec` in modalità low-latency, disegno su `SurfaceView`.
- **Codec iniziale H.264**, senza B-frame, con codifica hardware NVENC in preset a bassa latenza. HEVC si valuta solo se serve.
- **Trasporto: tunnel TCP su USB con `adb reverse`.** Funziona su qualsiasi cavo USB con Debug USB attivo, senza driver di rete. Alternative (USB tethering, modalità accessorio Android) restano al costruttore se `adb` si rivela un limite.
- **Monitor virtuale: driver open source Virtual Display Driver** (tecnologia IDD di Windows) invece di scriverne uno. L'installazione di un driver è la parte più delicata per stabilità e firma: va fatta con cautela e documentata, con la possibilità di rimuoverlo.
- **Principi di bassa latenza:** nessuna coda grande; se il tablet è in ritardo si scartano i frame vecchi invece di accumularli; cattura sincronizzata con il monitor virtuale; timestamp sui frame per misurare il ritardo vero.

## Tappe

0. **Prova minima.** Catturo un monitor virtuale (con il driver della tappa 1 installato solo per la prova, rimovibile), codifico, mando via cavo, mostro sul tablet. Misuro la latenza con un timestamp a schermo e il throughput del tunnel `adb`. Serve a decidere se l'architettura regge e a scoprire il problema delle due GPU.
1. **Monitor virtuale.** Installo il driver, il tablet diventa un monitor esteso, risoluzione e refresh scelti per il tablet.
2. **Rifinitura.** Riconnessione automatica, avvio con un clic, gestione dello spegnimento schermo, statistiche (fps, latenza).
3. **Opzionale.** Touch e S Pen come input in Windows.

## Scelte aggiuntive dopo il controllo del costruttore (mie, da correggere)

- **Cavo e `adb`.** Alla tappa 0 si misura il throughput reale del tunnel e il ritardo che aggiunge. Il bitrate previsto (qualche decina di Mbit/s) sta largamente dentro un cavo USB 2.0. Si passa a un trasporto alternativo (USB tethering o modalità accessorio) solo se il tunnel aggiunge più di circa 10 ms o non sostiene il doppio del bitrate scelto.
- **Soglia di latenza.** Il giudizio finale lo dai tu a occhio, perché è il fastidio che vuoi eliminare. Come riferimento tecnico, la tappa 0 è riuscita se sta sotto circa 60 ms e si sente nettamente meglio del wireless attuale. Se non ci arriva, prima si ottimizza (impostazioni encoder, code, GPU), poi si valuta HEVC o C++, e se ne discute con te.
- **Driver del monitor virtuale.** Uso solo una versione firmata, per non dover attivare la modalità test di Windows né disattivare Secure Boot. Se nessuna versione firmata regge il nostro modo di funzionare, mi fermo e te lo chiedo prima di toccare quelle impostazioni. Il programma sul PC controlla il driver all'avvio (aggiunge o toglie il monitor e imposta la modalità), senza passi manuali.
- **Due GPU.** La tappa 0 deve catturare un monitor virtuale, non uno reale, altrimenti il risultato non prevede la tappa 1. Si prova a legare il driver alla scheda NVIDIA. Se non basta, si confronta una copia tra schede con la codifica sull'AMD (AMF), e vince la misura.
- **Testo contro latenza.** Priorità alla latenza, con un bitrate alto, che sul cavo costa poco. Il formato 4:2:0 delle app Android va bene finché il testo resta leggibile. Se è sfocato, si alza il bitrate o si cambia profilo.
- **Risoluzione e refresh.** Si parte da 60 Hz alla risoluzione nativa verificata sul tablet, con pixel 1:1 e senza bande nere. I 90 Hz sono un tentativo della tappa 2, se la decodifica del tablet li regge.
- **"Spegnimento schermo" (tappa 2).** Vuol dire tenere il tablet sveglio mentre l'app è aperta (wake lock), non altro.
- **Librerie.** Dove possibile uso Media Foundation o NVENC tramite wrapper, per evitare dipendenze GPL. Per uso personale FFmpeg andrebbe comunque bene. Prima della tappa 0 controllo che nell'Android SDK ci siano piattaforma e build-tools, e se manca Gradle.

## Lasciato al costruttore

- Struttura del progetto e delle cartelle.
- Scelta delle librerie (wrapper Direct3D/DXGI, accesso a NVENC tramite Media Foundation o FFmpeg, ecc.).
- Formato dei messaggi sul cavo (framing) e gestione degli errori.
- Valori esatti di risoluzione, refresh e bitrate, da regolare con la misura.
- Come installare `adb` in modo comodo (usare quello dell'Android SDK già presente).

## Rischi principali

- Doppia GPU: possibili copie lente tra le due schede.
- Driver di monitor virtuale: firma, installazione, stabilità.
- `adb` può aggiungere ritardo al tunnel o richiedere di riautorizzare il PC.
- La cattura dello schermo può non vedere contenuti protetti.
