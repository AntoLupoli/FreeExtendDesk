# Schermo esteso via cavo (PC Windows -> Galaxy Tab S10 FE) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Far comparire il tablet come secondo monitor esteso di Windows via cavo USB-C, con latenza il più bassa possibile.

**Architecture:** Un driver di monitor virtuale open source crea il secondo monitor. Un host C# cattura quel monitor (Desktop Duplication), lo codifica in H.264 con encoder hardware e lo manda su TCP attraverso `adb reverse`. Un'app Kotlin sul tablet decodifica con `MediaCodec` in low-latency e disegna su una `SurfaceView`. Le due parti parlano un protocollo binario piccolo, con fixture "golden" condivise per garantire che C# e Kotlin si capiscano.

**Tech Stack:** C# / .NET 8 (host, Vortice.Windows per Direct3D11/DXGI/Media Foundation, xUnit), Kotlin + Gradle (Android, JUnit), `adb` dall'Android SDK, Virtual Display Driver (IDD, versione firmata).

**Spec:** `docs/superpowers/specs/2026-11-18-wired-extended-display-design.md`

**Fuori da questo piano:** la tappa 3 della spec (touch e S Pen verso Windows) è opzionale e avrà un piano a parte.

## Global Constraints

- Windows 11 Home; GPU doppia: NVIDIA RTX 3050 Laptop (NVENC) + AMD Radeon integrata.
- Host in C# (.NET 8); app Android in Kotlin con `MediaCodec` low-latency e `SurfaceView`.
- Codec H.264, senza B-frame, codifica hardware a bassa latenza, bitrate alto (priorità alla latenza sul testo).
- Trasporto: TCP su USB con `adb reverse`; porta `27183`; l'host ascolta su `127.0.0.1`, il tablet si connette a `127.0.0.1:27183`.
- `adb` è in `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe` (non nel PATH).
- Monitor virtuale: solo driver **firmato**. Non attivare modalità test di Windows né disattivare Secure Boot: se nessun driver firmato va bene, fermarsi e chiedere all'utente.
- L'utente esegue di persona l'installazione del driver e conferma i download (.NET SDK, driver). Niente modifiche a impostazioni di sistema da parte dell'agente.
- Si parte da 60 Hz alla risoluzione nativa del tablet, pixel 1:1, nessuna banda nera. I 90 Hz sono un tentativo della tappa 2.
- Latenza: obiettivo ordine di 30-60 ms; la tappa 0 è riuscita se sta sotto circa 60 ms e si sente meglio del wireless. Il tunnel `adb` non deve aggiungere più di circa 10 ms né mancare di sostenere il doppio del bitrate scelto.
- Il tablet resta sveglio mentre l'app è aperta (wake lock / `FLAG_KEEP_SCREEN_ON`).
- Niente audio, niente wireless, nessuna dipendenza GPL se evitabile (per uso personale FFmpeg sarebbe comunque ammesso).
- Dati in uscita: nessun invio a servizi esterni (solo download di strumenti).

## Struttura dei file

```
FreeExtendDex/
  .gitignore
  protocol/golden/                     # fixture binarie condivise C#/Kotlin
  tools/env.ps1                        # aggiunge adb al PATH per la sessione
  tools/virtual-display/               # note e script di installazione/rimozione driver
  host/FreeExtend.sln
  host/src/FreeExtend.Protocol/        # Message, MessageType, MessageCodec
  host/src/FreeExtend.Host/            # capture, encoder, server, adb, stats, launcher
  host/tests/FreeExtend.Protocol.Tests/
  host/tests/FreeExtend.Host.Tests/
  android/                             # progetto Gradle (app/ con Kotlin)
  docs/device-profile.md               # dati reali del tablet (T1)
  docs/measurements/stage0.md          # misure della tappa 0 (T9)
```

## Review Focus

Casi che la spec implica ma che nessun test "felice" esercita; ognuno ha il suo test nel task indicato.

1. Il cavo viene staccato e riattaccato a flusso in corso: l'host non si blocca, il tablet mostra "In attesa del PC", al ritorno il video riprende da un keyframe (T11).
2. Desktop fermo, nessun frame nuovo da Windows per secondi: il flusso non deve essere considerato morto né bloccarsi, e il primo frame dopo la pausa arriva subito (T5, T11).
3. Cambio risoluzione/DPI, blocco con Win+L, UAC o sospensione/riattivazione del PC: la cattura perde l'accesso (`DXGI_ERROR_ACCESS_LOST`) e deve ricrearsi, non far crashare l'host (T5).
4. Tablet lento o rete USB in ritardo: la latenza non deve crescere accumulando frame, si scartano quelli vecchi e si chiede un keyframe (T7).
5. Dati corrotti o payload enorme sul socket: l'host/l'app chiudono la connessione con un errore, senza esaurire la memoria (T3, T4).

---

## Tappa 0: prova minima

### Task 1: Strumenti, repository e profilo del tablet

**Files:**
- Create: `.gitignore`, `tools/env.ps1`, `docs/device-profile.md`
- Create: `host/FreeExtend.sln` (con i progetti vuoti dei task seguenti, o solo la sln qui)

**Interfaces:**
- Produces: `adb` raggiungibile in sessione dopo `. .\tools\env.ps1`; `dotnet --list-sdks` mostra 8.x; `git` inizializzato; `docs/device-profile.md` con risoluzione nativa, densità e refresh del tablet.

Stato di partenza verificato: `dotnet` ha solo i runtime (nessun SDK); l'`adb` dell'SDK funziona; il tablet (`<seriale>`) è visto ma "unauthorized"; Android SDK ha platform 34/35 e build-tools 34.0.0/36.0.0; il repository non è git.

- [ ] **Step 1: `git init` e `.gitignore`** (`bin/`, `obj/`, `.vs/`, `build/`, `.gradle/`, `*.apk`, `local.properties`). Primo commit con la cartella `docs/`.
- [ ] **Step 2: Chiedere all'utente il permesso di scaricare e installare il .NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8`). Verifica: `dotnet --list-sdks` stampa una riga `8.0.x`.
- [ ] **Step 3: `tools/env.ps1`** aggiunge `%LOCALAPPDATA%\Android\Sdk\platform-tools` al PATH della sessione. Verifica: dopo `. .\tools\env.ps1`, `adb version` risponde.
- [ ] **Step 4: Chiedere all'utente di accettare la richiesta "Consenti debug USB" sul tablet** (e di spuntare "Autorizza sempre"). Verifica: `adb devices` mostra `<seriale> device`.
- [ ] **Step 5: Registrare il profilo del tablet** in `docs/device-profile.md`: risoluzione (`adb shell wm size`), densità (`wm density`), refresh supportati (`adb shell dumpsys display`, righe `supportedModes`), versione Android e SoC (`getprop ro.build.version.release`, `ro.soc.model`). Questi valori sostituiscono le stime della spec (2304x1440, 90 Hz).
- [ ] **Step 6: Verificare gli strumenti Android**: esistono Gradle wrapper riutilizzabili in `%USERPROFILE%\.gradle\wrapper\dists`? Annotare nel profilo come si genererà il wrapper del progetto (T4). Se manca Gradle, chiedere il permesso di scaricarlo.
- [ ] **Step 7: Commit** (`chore: toolchain e profilo del tablet`).

### Task 2: Monitor virtuale (prerequisito della prova)

**Files:**
- Create: `tools/virtual-display/README.md`, `tools/virtual-display/remove.ps1` (e eventuale `install.ps1` che stampa solo i passi)

**Interfaces:**
- Produces: Windows elenca un secondo monitor; `docs/device-profile.md` annota **quale scheda grafica** (NVIDIA o AMD) lo ospita e quali modi supporta (risoluzione e refresh).

- [ ] **Step 1: Scegliere una versione firmata** del Virtual Display Driver (progetto open source, IDD). Nel README annotare nome, versione, origine, come verificare la firma (`Get-AuthenticodeSignature` sui file `.sys`/`.dll`/`.cat`) e il checksum. Se non ce n'è una firmata adatta, fermarsi e chiederlo all'utente.
- [ ] **Step 2: Mostrare all'utente cosa verrà installato** (nome, fonte, dimensione) e chiedere conferma al download. L'**utente** esegue l'installazione; l'agente non cambia impostazioni di sistema.
- [ ] **Step 3: Verifica**: `[System.Windows.Forms.Screen]::AllScreens` mostra due schermi; in Impostazioni > Schermo il secondo è estendibile. Annotare la scheda che lo ospita (Gestione dispositivi o `dxdiag`) e il modo scelto (risoluzione del tablet a 60 Hz; se il driver non la offre, il modo più vicino e perché).
- [ ] **Step 4: `remove.ps1`** documenta e automatizza la rimozione del driver. Verifica: lo script (o i passi nel README) è stato letto dall'utente prima di proseguire.
- [ ] **Step 5: Commit** (`docs: monitor virtuale e procedura di rimozione`).

### Task 3: Protocollo del flusso (C#) con fixture golden

**Files:**
- Create: `host/src/FreeExtend.Protocol/Message.cs`, `MessageCodec.cs`, `ProtocolException.cs`
- Create: `protocol/golden/hello.bin`, `protocol/golden/video_key.bin`, `protocol/golden/README.md`
- Test: `host/tests/FreeExtend.Protocol.Tests/MessageCodecTests.cs`

**Interfaces:**
- Produces:
  - `enum MessageType : byte { Hello = 1, Config = 2, Video = 3, Ping = 4, Pong = 5, Rendered = 6, RequestKeyframe = 7 }`
  - `readonly record struct Message(MessageType Type, byte Flags, long TimestampUs, ReadOnlyMemory<byte> Payload)`; `Flags` bit 0 = keyframe (solo `Video`)
  - `static class MessageCodec { const int HeaderSize = 16; const int MaxPayload = 8 * 1024 * 1024; static void Write(Stream s, Message m); static Message? Read(Stream s); static readonly byte[] Magic; }` — `Read` restituisce `null` a fine flusso pulita sul confine di un messaggio; altrimenti lancia `ProtocolException` (troncato, tipo sconosciuto, payload > `MaxPayload`).
  - `static class HelloPayload { static byte[] Encode(ushort width, ushort height, ushort refreshHz, byte codecMask); static (ushort W, ushort H, ushort Hz, byte Codecs) Decode(ReadOnlySpan<byte>) }` e `ConfigPayload { Encode(ushort width, ushort height, ushort fps, ReadOnlySpan<byte> annexBParamSets); Decode(...) }`.

Formato (big-endian): intestazione di 16 byte = `u8 type | u8 flags | u16 0 | u32 payloadLength | u64 timestampUs`, poi il payload. Ogni connessione inizia con i 4 byte `Magic` = ASCII `FXD1` inviati da entrambi i lati; se non coincidono si chiude. Payload `Hello` = `u16 width, u16 height, u16 refreshHz, u8 codecMask` (bit 0 = H.264). Payload `Config` = `u16 width, u16 height, u16 fps` + parametri SPS/PPS in Annex-B.

- [ ] **Step 1: Scrivere il test fallito** (xUnit). Nomi e asserzioni:
  - `Hello_encodes_to_golden_bytes`: `Message(Hello, 0, 0, HelloPayload.Encode(2304, 1440, 60, 1))` scritto su `MemoryStream` è uguale byte per byte a `01 00 00 00 00 00 00 07 00 00 00 00 00 00 00 00 09 00 05 A0 00 3C 01` e al file `protocol/golden/hello.bin`.
  - `Video_key_roundtrips_with_timestamp`: scrittura e lettura di un `Video` con `Flags = 1`, `TimestampUs = 1_234_567`, payload `00 00 00 01 65 AA` restituiscono lo stesso messaggio (il test scrive anche `video_key.bin` se manca, poi lo confronta).
  - `Read_returns_null_on_clean_eof` e `Read_throws_on_truncated_header_or_payload`.
  - `Read_throws_on_unknown_type` e `Read_throws_on_payload_over_max_without_allocating` (lunghezza dichiarata `MaxPayload + 1`, il flusso non contiene altro: l'eccezione arriva prima di allocare).
- [ ] **Step 2: Eseguire** `dotnet test host/tests/FreeExtend.Protocol.Tests` e verificare che fallisca (tipi non definiti).
- [ ] **Step 3: Implementare** i tipi sopra in `FreeExtend.Protocol`. `Read` legge l'intestazione con un helper che accumula fino a 16 byte (il flusso TCP restituisce letture parziali) e valida prima di allocare.
- [ ] **Step 4: Eseguire** i test e verificare che passino.
- [ ] **Step 5: Commit** (`feat: protocollo del flusso con fixture golden`).

### Task 4: Progetto Android e protocollo in Kotlin

**Files:**
- Create: `android/` (progetto Gradle; `applicationId = "it.freeextend.display"`, `minSdk = 33`, `compileSdk = 35`), `android/app/src/main/java/it/freeextend/display/Message.kt`, `MessageCodec.kt`
- Test: `android/app/src/test/java/it/freeextend/display/MessageCodecTest.kt`

**Interfaces:**
- Consumes: fixture `protocol/golden/*.bin` (T3).
- Produces: `data class Message(val type: Int, val flags: Int, val timestampUs: Long, val payload: ByteArray)`; `object MessageCodec { fun write(out: OutputStream, m: Message); fun read(input: InputStream): Message?; val MAGIC: ByteArray }` con le stesse regole di T3 (`null` a EOF pulito; `ProtocolException` per troncato/tipo sconosciuto/payload oltre 8 MiB, senza allocare); `object HelloPayload`, `object ConfigPayload` con `encode`/`decode` equivalenti.

- [ ] **Step 1: Creare il progetto Gradle** secondo quanto annotato in T1 step 6. Verifica: `gradlew assembleDebug` termina con `BUILD SUCCESSFUL`.
- [ ] **Step 2: Scrivere il test fallito** (JUnit): `hello_matches_golden_bytes` (legge `../../protocol/golden/hello.bin` e confronta con `MessageCodec.write` di `Hello(2304,1440,60,1)`), `video_key_matches_golden_bytes`, `read_returns_null_on_clean_eof`, `read_throws_on_unknown_type`, `read_throws_on_payload_over_max`, `read_handles_partial_reads` (un `InputStream` che restituisce 1 byte per volta).
- [ ] **Step 3: Eseguire** `gradlew testDebugUnitTest` e verificare che fallisca.
- [ ] **Step 4: Implementare** `Message.kt` e `MessageCodec.kt`. Lettura con `DataInputStream.readFully`.
- [ ] **Step 5: Eseguire** i test e verificare che passino.
- [ ] **Step 6: Commit** (`feat: progetto Android e protocollo Kotlin`).

### Task 5: Cattura del monitor virtuale (Desktop Duplication)

**Files:**
- Create: `host/src/FreeExtend.Host/Capture/OutputInfo.cs`, `CapturedFrame.cs`, `CaptureClock.cs`, `IFrameSource.cs`, `DesktopDuplicationSource.cs`
- Test: `host/tests/FreeExtend.Host.Tests/CaptureClockTests.cs`

**Interfaces:**
- Produces:
  - `record OutputInfo(string DeviceName, string AdapterName, int Width, int Height, bool AttachedToDesktop)`
  - `readonly record struct CapturedFrame(ID3D11Texture2D Texture, long CaptureUs)`
  - `static class CaptureClock { static long NowUs(); }` (orologio monotono dell'host, `Stopwatch` convertito in microsecondi)
  - `interface IFrameSource : IDisposable { bool TryAcquireFrame(int timeoutMs, out CapturedFrame frame); void ReleaseFrame(); }` — `false` se non c'è nulla di nuovo entro il timeout (desktop fermo: **non** è un errore).
  - `DesktopDuplicationSource(string? outputDeviceName)` con `static IReadOnlyList<OutputInfo> ListOutputs()`.

- [ ] **Step 1: Test fallito** `CaptureClockTests.NowUs_is_monotonic_and_microsecond_scaled`: due letture separate da `Thread.Sleep(20)` differiscono di 19.000-60.000 µs.
- [ ] **Step 2: Eseguire** e verificare che fallisca; **Step 3: implementare** `CaptureClock`; **Step 4: test verde**.
- [ ] **Step 5: Implementare `DesktopDuplicationSource`** con Vortice.Windows su Direct3D11/DXGI 1.2. Seleziona l'output per nome (il monitor virtuale di T2); sullo stesso adattatore dell'output crea il device D3D11. Su `DXGI_ERROR_ACCESS_LOST` ricrea la duplicazione (con tentativi a intervalli brevi) invece di lanciare; su `DXGI_ERROR_WAIT_TIMEOUT` restituisce `false`.
- [ ] **Step 6: Verifica manuale** con una piccola modalità `FreeExtend.Host.exe capture-test --output <nome> --seconds 10` che stampa: elenco output, fps effettivi mentre si trascina una finestra sul monitor virtuale, e salva un PNG di un frame. Atteso: l'immagine mostra il contenuto del monitor virtuale; fps vicini al refresh quando c'è movimento; zero eccezioni.
- [ ] **Step 7: Verifica di robustezza** (Review Focus 2 e 3): durante il test, bloccare con Win+L e sbloccare, poi cambiare la risoluzione del monitor virtuale. Atteso: il test continua dopo il ripristino, senza crash.
- [ ] **Step 8: Commit** (`feat: cattura Desktop Duplication del monitor virtuale`).

### Task 6: Encoder H.264 hardware a bassa latenza

**Files:**
- Create: `host/src/FreeExtend.Host/Encode/EncoderConfig.cs`, `EncodedPacket.cs`, `IVideoEncoder.cs`, `MfH264Encoder.cs`, `NalInspector.cs`
- Test: `host/tests/FreeExtend.Host.Tests/NalInspectorTests.cs`

**Interfaces:**
- Consumes: `CapturedFrame` (T5).
- Produces:
  - `record EncoderConfig(int Width, int Height, int Fps, int BitrateKbps)`
  - `readonly record struct EncodedPacket(ReadOnlyMemory<byte> Data, bool IsKeyframe, long CaptureUs)`
  - `interface IVideoEncoder : IDisposable { byte[] ParameterSets { get; } void Encode(CapturedFrame frame, bool forceKeyframe); event Action<EncodedPacket> PacketReady; }` — `ParameterSets` = SPS+PPS in Annex-B.
  - `static class NalInspector { static IReadOnlyList<NalUnit> Parse(ReadOnlySpan<byte> annexB); }` con `readonly record struct NalUnit(int Type, int Offset, int Length)`.

- [ ] **Step 1: Test fallito** `NalInspectorTests`: `Parse_finds_sps_pps_idr` su `00 00 00 01 67 AA 00 00 00 01 68 BB 00 00 01 65 CC` restituisce tipi `[7, 8, 5]` con offset e lunghezze corretti; `Parse_returns_empty_without_start_codes`.
- [ ] **Step 2: Eseguire**, vedere fallire; **Step 3: implementare** `NalInspector`; **Step 4: verde**.
- [ ] **Step 5: Implementare `MfH264Encoder`** con l'encoder H.264 hardware di Media Foundation (MFT NVIDIA) con input texture D3D11 tramite `IMFDXGIDeviceManager`: profilo High, CBR con `BitrateKbps` alto, **nessun B-frame**, `CODECAPI_AVLowLatencyMode` attivo, GOP lungo con keyframe a richiesta (`forceKeyframe`), un pacchetto in uscita per frame in ingresso, in ordine. Se la texture è su un'altra scheda rispetto all'encoder, copiarla sull'adattatore dell'encoder e misurare il costo (T9 lo riporta). Libreria alternativa lasciata al costruttore se Media Foundation si rivela un limite.
- [ ] **Step 6: Verifica manuale** `encode-test --seconds 10 --out out.h264` che usa la cattura di T5 e scrive il flusso grezzo. Atteso: il primo pacchetto contiene SPS(7)+PPS(8)+IDR(5) secondo `NalInspector`; i pacchetti escono nello stesso ordine degli ingressi (timestamp crescenti); tempo medio di codifica per frame stampato, obiettivo sotto circa 5 ms; il file è non vuoto.
- [ ] **Step 7: Commit** (`feat: encoder H.264 hardware a bassa latenza`).

### Task 7: Server del flusso, tunnel `adb` e controllo del backlog

**Files:**
- Create: `host/src/FreeExtend.Host/Net/StreamServer.cs`, `SendBacklogPolicy.cs`, `AdbTunnel.cs`
- Test: `host/tests/FreeExtend.Host.Tests/SendBacklogPolicyTests.cs`, `AdbTunnelTests.cs`, `StreamServerTests.cs`

**Interfaces:**
- Consumes: `Message`, `MessageCodec`, `HelloPayload`, `ConfigPayload` (T3); `EncodedPacket` (T6).
- Produces:
  - `enum DeviceState { None, Unauthorized, Ready }`; `static class AdbTunnel { static string AdbPath(); static DeviceState ParseDevices(string adbDevicesOutput); static DeviceState Current(); static void EnsureReverse(int port); }` (`EnsureReverse` esegue `adb reverse tcp:<port> tcp:<port>`).
  - `enum BacklogDecision { Send, DropAndRequestKeyframe }`; `sealed class SendBacklogPolicy { BacklogDecision OnPacket(int queuedPackets, bool isKeyframe); }` — regola: se `queuedPackets >= 2` e il pacchetto non è keyframe, si scarta e si chiede un keyframe; i keyframe si inviano sempre e svuotano la coda dei pacchetti più vecchi.
  - `sealed class StreamServer : IAsyncDisposable { StreamServer(int port, ConfigProvider config); Task RunAsync(CancellationToken ct); void Publish(EncodedPacket p); bool TakeKeyframeRequest(); event Action<(ushort W, ushort H, ushort Hz)> ClientConnected; event Action<long> RenderedAck; event Action<long, long> PongReceived; }` — un client alla volta; handshake `Magic` + `Hello` dal tablet, risposta `Magic` + `Config`.

- [ ] **Step 1: Test falliti**:
  - `AdbTunnelTests.ParseDevices_unauthorized`: l'output reale di questa macchina (`List of devices attached\n<seriale>            unauthorized transport_id:1\n`) dà `Unauthorized`; con `device` dà `Ready`; con solo l'intestazione dà `None`.
  - `SendBacklogPolicyTests`: `queued=0, delta` -> `Send`; `queued=2, delta` -> `DropAndRequestKeyframe`; `queued=5, keyframe` -> `Send`.
  - `StreamServerTests.Handshake_then_config_then_video`: un client TCP di prova su loopback invia `Magic` + `Hello(2304,1440,60,1)`, riceve `Magic` + `Config` e poi, dopo `Publish`, un `Video` con lo stesso `TimestampUs` e flag keyframe.
  - `StreamServerTests.Bad_magic_closes_connection` e `Oversized_payload_closes_connection` (Review Focus 5): il server chiude e resta in ascolto.
- [ ] **Step 2: Eseguire** e vedere fallire; **Step 3: implementare**; **Step 4: test verdi**.
- [ ] **Step 5: Verifica del tunnel e del throughput** (da eseguire dopo il Task 8, quando l'app esiste): `FreeExtend.Host.exe tunnel-test` esegue `EnsureReverse`, poi per 20 secondi invia `Ping` con payload di 64 KiB; l'app risponde a ogni `Ping` con un `Pong` che ripete il timestamp. L'host stampa MB/s sostenuti (byte inviati / tempo fino all'ultimo `Pong`) e l'RTT p50. Annotare i valori in `docs/measurements/stage0.md` (T9).
- [ ] **Step 6: Commit** (`feat: server di flusso, tunnel adb e controllo backlog`).

### Task 8: App Android: client, decoder e schermo

**Files:**
- Create: `android/app/src/main/java/it/freeextend/display/StreamClient.kt`, `VideoDecoder.kt`, `MainActivity.kt`; `AndroidManifest.xml` (activity landscape, a schermo intero)
- Test: `android/app/src/test/java/it/freeextend/display/StreamClientTest.kt`

**Interfaces:**
- Consumes: `MessageCodec`, `HelloPayload`, `ConfigPayload` (T4); server `StreamServer` (T7).
- Produces:
  - `class StreamClient(host: String = "127.0.0.1", port: Int = 27183, private val listener: Listener) { interface Listener { fun onConfig(width: Int, height: Int, fps: Int, paramSets: ByteArray); fun onVideo(annexB: ByteArray, keyframe: Boolean, timestampUs: Long); fun onDisconnected() }; fun start(hello: Hello); fun send(message: Message); fun stop() }`
  - `StreamClient` risponde a ogni `Ping` con un `Pong` che ripete `TimestampUs`, senza toccare il payload.
  - `class VideoDecoder(surface: Surface, onRendered: (timestampUs: Long) -> Unit) { fun configure(width: Int, height: Int, paramSets: ByteArray); fun queue(annexB: ByteArray, timestampUs: Long); fun release() }` — `MediaFormat` con `KEY_LOW_LATENCY = 1`, `KEY_PRIORITY = 0`, `KEY_OPERATING_RATE = Short.MAX_VALUE`; `onFrameRendered` invia `Rendered` all'host con lo stesso timestamp.
  - `MainActivity`: schermo intero immersivo, `FLAG_KEEP_SCREEN_ON`, `SurfaceView`, invia `Hello` con la risoluzione reale della finestra e il refresh del display.

- [ ] **Step 1: Test fallito** `StreamClientTest.receives_config_and_video` (JUnit, `ServerSocket` su loopback che fa da finto host): dopo l'handshake il client riceve `onConfig` con 2304x1440/60 e i parametri attesi, poi `onVideo` con timestamp e flag giusti.
- [ ] **Step 2: Eseguire** `gradlew testDebugUnitTest --tests '*StreamClientTest*'` e vedere fallire; **Step 3: implementare** `StreamClient` (thread di lettura, connessione una volta; la riconnessione arriva in T11); **Step 4: verde**.
- [ ] **Step 5: Implementare `VideoDecoder` e `MainActivity`** come da interfacce. Un thread solo per svuotare l'output del codec; `releaseOutputBuffer(index, true)` per ogni frame, senza accodare.
- [ ] **Step 6: Installare e provare**: `gradlew installDebug`, poi `adb reverse tcp:27183 tcp:27183`, `FreeExtend.Host.exe stream` (cattura + codifica + server). Atteso: il tablet mostra il monitor virtuale; trascinando una finestra, si muove sul tablet.
- [ ] **Step 7: Commit** (`feat: app Android con client e decoder low-latency`).

### Task 9: Statistiche di latenza e verifica della tappa 0

**Files:**
- Create: `host/src/FreeExtend.Host/Stats/LatencyStats.cs`, `StatsReporter.cs`, `docs/measurements/stage0.md`
- Test: `host/tests/FreeExtend.Host.Tests/LatencyStatsTests.cs`

**Interfaces:**
- Consumes: eventi `RenderedAck` e `PongReceived` del server (T7); `CaptureClock.NowUs()`.
- Produces:
  - `readonly record struct StatsSnapshot(double Fps, double RttMs, double PipelineP50Ms, double PipelineP95Ms, double EncodeMsAvg, long DroppedFrames)`
  - `sealed class LatencyStats { void OnPong(long sentUs, long recvUs); void OnRendered(long captureUs, long nowUs); void OnFrameSent(); void OnEncode(double ms); void OnDropped(); StatsSnapshot Snapshot(); }` — `pipeline = (now - captureUs) - rtt/2`, percentili su una finestra mobile di 5 secondi.
  - `StatsReporter` stampa una riga al secondo in console e invia un `Ping` al tablet ogni secondo.

- [ ] **Step 1: Test fallito** `LatencyStatsTests`: `Pipeline_subtracts_half_rtt` (RTT 10 ms, frame reso 40 ms dopo la cattura -> p50 = 35 ms), `Percentiles_over_window` (100 campioni 1..100 ms -> p50 = 50, p95 = 95), `Fps_counts_frames_in_last_second`.
- [ ] **Step 2: Eseguire**, vedere fallire; **Step 3: implementare**; **Step 4: verde**.
- [ ] **Step 5: Misurare** per 60 secondi in tre scenari: desktop fermo, scorrimento di testo, video a schermo intero sul monitor virtuale. Annotare in `docs/measurements/stage0.md`: p50/p95 della pipeline, RTT del tunnel, throughput di T7, tempo di codifica medio, frame scartati, scheda grafica usata per cattura e codifica e il costo di un'eventuale copia tra schede.
- [ ] **Step 6: Controllo incrociato dal vivo**: l'utente filma con il telefono a rallentatore un cronometro in millisecondi sul monitor virtuale mostrato sul tablet e lo stesso cronometro sul monitor fisico; annotare la differenza.
- [ ] **Step 7: Cancello della tappa 0**: confrontare con i criteri nei Global Constraints. Se non si rispettano, ottimizzare (impostazioni encoder, code, scheda) e rimisurare; se ancora no, valutare HEVC, C++ o un altro trasporto **e discuterne con l'utente prima di proseguire**. Riportare in ogni caso i numeri all'utente.
- [ ] **Step 8: Commit** (`feat: statistiche di latenza e misure della tappa 0`).

---

## Tappa 1: monitor esteso a un clic

### Task 10: Controllo del monitor virtuale e avvio a un clic

**Files:**
- Create: `host/src/FreeExtend.Host/Display/ModeSelector.cs`, `IVirtualDisplay.cs`, `VirtualDisplayDriver.cs`; `Start-FreeExtend.ps1`
- Test: `host/tests/FreeExtend.Host.Tests/ModeSelectorTests.cs`

**Interfaces:**
- Consumes: `HelloPayload` (T3), modi supportati dal driver (annotati in T2).
- Produces:
  - `record DisplayMode(int Width, int Height, int RefreshHz)`
  - `static class ModeSelector { static DisplayMode Choose(DisplayMode requested, IReadOnlyList<DisplayMode> supported); }` — modo esatto se esiste; altrimenti il più grande con lo stesso rapporto d'aspetto non superiore al richiesto e refresh <= richiesto; se non ce n'è, solleva `NoSuitableModeException`.
  - `interface IVirtualDisplay { Task<OutputInfo> EnsureAsync(DisplayMode mode, CancellationToken ct); Task RemoveAsync(); }`; `VirtualDisplayDriver : IVirtualDisplay` parla con il driver scelto in T2 (il meccanismo, per esempio file di configurazione o pipe del driver, lo decide il costruttore).
  - `Start-FreeExtend.ps1`: controlla `AdbTunnel.Current()` (messaggio chiaro se il tablet è "unauthorized"), esegue `EnsureReverse`, avvia l'host; alla chiusura rimuove il monitor virtuale.

- [ ] **Step 1: Test fallito** `ModeSelectorTests`: `Exact_match_wins` (2304x1440@60 in elenco), `Falls_back_to_same_aspect_ratio_not_larger` (richiesto 2304x1440, elenco con 1920x1200 e 2560x1600 -> 1920x1200), `Refresh_not_above_request` (richiesto 60, elenco con 90 e 60 -> 60), `Throws_when_nothing_fits`.
- [ ] **Step 2: Eseguire**, vedere fallire; **Step 3: implementare**; **Step 4: verde**.
- [ ] **Step 5: Implementare `VirtualDisplayDriver`** e collegare nell'host: all'arrivo del `Hello` si sceglie il modo con `ModeSelector`, si crea il monitor, si cattura *quello* e si riavvia la codifica con le dimensioni scelte. Verifica: connettendo il tablet, Windows lo mostra come secondo monitor alla risoluzione del tablet a 60 Hz; staccando l'host, il monitor sparisce.
- [ ] **Step 6: Verifica "un clic"**: da PC con cavo collegato e app sul tablet aperta, `Start-FreeExtend.ps1` basta; nessun passo manuale a parte aprire l'app. Annotare nel README come si fa.
- [ ] **Step 7: Commit** (`feat: monitor virtuale controllato dall'host e avvio a un clic`).

---

## Tappa 2: rifinitura

### Task 11: Riconnessione e resistenza ai problemi

**Files:**
- Modify: `host/src/FreeExtend.Host/Net/StreamServer.cs`, `android/.../StreamClient.kt`, `MainActivity.kt`
- Test: `host/tests/FreeExtend.Host.Tests/StreamServerReconnectTests.cs`, `android/.../StreamClientReconnectTest.kt`

**Interfaces:**
- Consumes: `StreamServer`, `StreamClient` (T7, T8).
- Produces: `StreamServer` accetta un nuovo client dopo la caduta del precedente e per ogni connessione invia `Config` e un keyframe (`TakeKeyframeRequest()` vero); keepalive: l'host invia un `Ping` almeno ogni secondo anche a desktop fermo, e un client senza traffico da 3 secondi considera la connessione morta; `StreamClient` riprova con pausa crescente 500 ms -> 2 s e mostra il testo "In attesa del PC" finché non si riconnette.

- [ ] **Step 1: Test falliti**: `Reconnect_gets_config_and_keyframe_first` (client si connette, cade, si riconnette: il primo `Video` ricevuto ha flag keyframe e prima c'è `Config`); `Idle_server_sends_ping_within_1s`; Kotlin: `reconnects_after_server_closes` e `declares_dead_after_3s_silence`.
- [ ] **Step 2: Eseguire**, vedere fallire; **Step 3: implementare**; **Step 4: verdi**.
- [ ] **Step 5: Verifica manuale** (Review Focus 1): staccare e riattaccare il cavo a flusso in corso, spegnere e riaccendere lo schermo del tablet, chiudere e riaprire l'app. Atteso: l'host non si blocca, il tablet riprende il video entro pochi secondi, senza riavviare l'host. Provare anche sospensione/riattivazione del PC.
- [ ] **Step 6: Commit** (`feat: riconnessione e keepalive`).

### Task 12: Tentativo a 90 Hz

**Files:**
- Modify: `host/src/FreeExtend.Host/Display/ModeSelector.cs` (richiesta del refresh massimo), `host/.../Stats/StatsReporter.cs` (frame persi del decoder)

**Interfaces:**
- Consumes: `Hello.refreshHz` dal tablet; modi 90 Hz del driver (se offerti).

- [ ] **Step 1: Test fallito** `ModeSelectorTests.Prefers_90hz_when_enabled_and_supported`: con l'opzione `PreferHighRefresh = true` e modi 60/90 sceglie 90; con `false` sceglie 60.
- [ ] **Step 2: Eseguire**, vedere fallire; **Step 3: implementare**; **Step 4: verde**.
- [ ] **Step 5: Misura a 90 Hz** per 60 secondi nei tre scenari di T9. Atteso: frame scartati < 1% e latenza p95 non peggiore di quella a 60 Hz. Se non regge, restare a 60 Hz di default e annotare il motivo in `docs/measurements/stage0.md`.
- [ ] **Step 6: Commit** (`feat: tentativo a 90 Hz con misura`).
