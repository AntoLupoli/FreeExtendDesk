# FreeExtend GUI e interfaccia in inglese: piano di implementazione

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** una finestra Windows Forms leggera che mostra lo stato di FreeExtend, avvia/ferma la trasmissione, risolve i requisiti mancanti e disinstalla tutto; e tutta l'interfaccia utente del progetto in inglese.

**Architecture:** nuovo progetto `FreeExtend.Gui` (WinExe) accanto a `FreeExtend.Host`; la logica pura (stato, percorsi, messaggi) e testata, i servizi (adb, processo `stream`, script elevati) sono sottili. La GUI non cattura ne codifica: avvia `FreeExtend.Host.exe stream --status --stop-on-stdin` come processo figlio nascosto, legge righe di stato leggibili dalla macchina e lo ferma scrivendo `stop` sul suo stdin (la chiusura dello stdin lo ferma comunque).

**Tech Stack:** .NET 8 (`net8.0-windows`, Windows Forms, nessun pacchetto NuGet nuovo), xUnit, PowerShell 5.1, Kotlin (solo 4 stringhe).

**Spec:** `docs/superpowers/specs/2026-11-19-gui-and-english-design.md`

## Global Constraints

- Finestra e README in **inglese**; anche testi dell'app Android, messaggi di `Start-FreeExtend.ps1`, `tools/**/*.ps1` e output/eccezioni del programma sul PC. NON si traducono `docs/` e i commenti nel codice.
- La finestra gira **senza privilegi**; l'amministratore (UAC) solo per `install.ps1` e `remove.ps1`.
- Nessun pacchetto NuGet nuovo; nessuna icona nella barra, nessun avvio con Windows, nessun avvio automatico al collegamento del cavo.
- Nessuna scrittura nel registro/installazione senza un click esplicito sul pulsante relativo.
- L'eseguibile dell'host e SEMPRE `host\src\FreeExtend.Host\bin\Debug\net8.0-windows\FreeExtend.Host.exe` (la preferenza grafica e legata a quel percorso).
- Porta host 27183, pacchetto Android `it.freeextend.display`.
- Nessuno screenshot con dati personali: ritagliare alla sola finestra, guardare, cancellare.

## Review Focus

1. `adb` assente dal PC: la finestra non si blocca ne va in crash, mostra come risolvere (Task 4 e 5).
2. `adb` lento o appeso (timeout 15 s): la finestra non si congela; ogni chiamata adb e fuori dal thread dell'interfaccia (Task 5, 6).
3. Due finestre aperte insieme litigherebbero per la porta 27183: la seconda si rifiuta di partire (Task 6, prova dal vivo).
4. Finestra chiusa o in crash durante la trasmissione: il monitor virtuale viene staccato (Task 2, 6, 11).
5. Percorsi con spazi/apostrofi e UAC rifiutato: script lanciati con quoting corretto, "annullato" non e un errore (Task 4, 5).

---

### Task 1: Primitive dell'host per la GUI (stato, stop, adb, schede)

**Files:**
- Create: `host/src/FreeExtend.Host/Net/HostStatusLine.cs`, `host/src/FreeExtend.Host/StdinStopWatcher.cs`, `host/src/FreeExtend.Host/Display/GpuAdapters.cs`
- Modify: `host/src/FreeExtend.Host/Net/AdbTunnel.cs` (`Run` da `private` a `public`)
- Test: `host/tests/FreeExtend.Host.Tests/HostStatusLineTests.cs`, `StdinStopWatcherTests.cs`, `GpuAdaptersTests.cs`

**Interfaces:**
- Produces (namespace `FreeExtend.Host.Net`):
  ```csharp
  public enum HostStatusKind { Waiting, Connected, Fps, Disconnected }
  public readonly record struct HostStatus(HostStatusKind Kind, int Width = 0, int Height = 0, int Hz = 0, double Fps = 0);
  public static class HostStatusLine {
      public const string Prefix = "@@fx ";
      public static string Format(HostStatus s);
      public static bool TryParse(string? line, out HostStatus status);
  }
  ```
  Formato: `@@fx waiting`, `@@fx connected 2304x1440@60`, `@@fx fps 59.8` (cultura invariante), `@@fx disconnected`.
- Produces: `internal static class StdinStopWatcher { public static Thread Start(TextReader input, Action onStop); }` (namespace `FreeExtend.Host`).
- Produces: `public static class GpuAdapters { public static bool IsRealAdapter(string name); public static IReadOnlyList<string> ListNames(); }` (namespace `FreeExtend.Host.Display`).
- Produces: `public static (int ExitCode, string Output) AdbTunnel.Run(string args, int timeoutMs)`.

- [ ] **Step 1: Write the failing tests**
  - `HostStatusLineTests`: `Format` e `TryParse` fanno il round trip per le quattro forme; `TryParse("@@fx connected 2304x1440@60")` da `(Connected, 2304, 1440, 60)`; `TryParse("@@fx fps 59.8")` da `Fps == 59.8` anche con `CultureInfo.CurrentCulture = it-IT` (ripristinarla nel test); `TryParse` e false per `null`, `""`, `"hello"`, `"@@fx bogus"`, `"@@fx connected abc"`.
  - `StdinStopWatcherTests` (attese con `ManualResetEventSlim.Wait(5000)`): `new StringReader("stop\n")` chiama `onStop` una volta; `"hello\nSTOP \n"` la chiama (confronto senza maiuscole, dopo `Trim`); un reader gia a fine flusso (`new StringReader("")`) la chiama; un `TextReader` che lancia `IOException` la chiama; se `onStop` lancia, il thread termina senza propagare; `onStop` non viene mai chiamata due volte.
  - `GpuAdaptersTests`: `IsRealAdapter` e true per `"NVIDIA GeForce RTX 3050 Laptop GPU"`, `"AMD Radeon(TM) Graphics"`, `"Intel(R) UHD Graphics"`; false per `"Microsoft Basic Render Driver"`, `"Virtual Display Driver by MTT"`, `"Parsec Virtual Display Adapter"`, `"Remote Display Adapter"`, `""`.
- [ ] **Step 2: Run to verify failure**: `dotnet test host\tests\FreeExtend.Host.Tests --filter "HostStatusLine|StdinStopWatcher|GpuAdapters"`; atteso: errore di compilazione (tipi mancanti).
- [ ] **Step 3: Implement** i tre tipi come da Interfaces. `StdinStopWatcher`: thread in background (`IsBackground = true`, nome `stdin-stop`) che legge righe con `ReadLine`; `null` (fine flusso) o eccezione di lettura => stop; riga `stop` => stop; altre righe ignorate; `onStop` chiamata al piu una volta e le sue eccezioni sono ingoiate. `GpuAdapters.IsRealAdapter`: stessa esclusione degli script (`Microsoft Basic|Remote|Virtual Display|Parsec|Meta Virtual|Mirage`, senza maiuscole) e nome non vuoto. `ListNames`: `DXGI.CreateDXGIFactory1<IDXGIFactory1>()` + `EnumAdapters1`, nomi distinti che passano `IsRealAdapter` (stesso uso di Vortice gia in `EncoderProbe.CreateNvidiaDevice`). Rendere `AdbTunnel.Run` pubblico.
- [ ] **Step 4: Run to verify pass**: stesso comando; atteso: PASS.
- [ ] **Step 5: Commit**: `git add host && git commit -m "feat(host): status lines, stdin stop watcher and adapter listing for the GUI"`

---

### Task 2: `stream` con `--status` e `--stop-on-stdin`

**Files:**
- Modify: `host/src/FreeExtend.Host/StreamCommand.cs` (parsing opzioni righe 52-62; emissione stato nei punti gia presenti)

**Interfaces:**
- Consumes: `HostStatusLine.Format`, `HostStatus`, `StdinStopWatcher.Start` (Task 1).
- Produces: due nuove opzioni di `stream`: `--status` (stampa su stdout le righe `@@fx ...`: `waiting` dopo `server.Start()`, `connected WxH@Hz` nel gestore `ClientConnected`, `disconnected` dove oggi stampa "Tablet disconnesso", `fps <valore>` nel resoconto di ogni secondo con `s.Fps`) e `--stop-on-stdin` (dopo la creazione di `cts`: `StdinStopWatcher.Start(Console.In, cts.Cancel)`). Senza le opzioni il comportamento e identico a oggi.

- [ ] **Step 1: Implement** le due opzioni; una funzione locale `void Status(HostStatus s)` stampa solo se `--status`. Nessuna modifica al resto della logica.
- [ ] **Step 2: Build e test**: `dotnet build host\FreeExtend.sln -v q` e `dotnet test host\FreeExtend.sln`; atteso: build OK, test verdi (15 + 89 + i nuovi del Task 1).
- [ ] **Step 3: Prova dal vivo dello stop** (tablet collegato non necessario, `--no-adb`): script PowerShell in `$env:TEMP`-scratchpad che avvia `FreeExtend.Host.exe stream --no-adb --status --stop-on-stdin` con `RedirectStandardInput/Output`, legge stdout fino a `@@fx waiting`, scrive `stop`, attende al massimo 10 s. Atteso: exit code 0 e `FreeExtend.Host.exe list-outputs` mostra il monitor virtuale con `attached=False`.
- [ ] **Step 4: Prova dal vivo della chiusura dello stdin**: stesso script ma chiude lo stdin invece di scrivere `stop`. Atteso: stesso risultato (exit 0, monitor non attaccato) entro 10 s.
- [ ] **Step 5: Commit**: `git commit -am "feat(host): stream --status and --stop-on-stdin"`

---

### Task 3: Script: `install-app.ps1`, `Start-FreeExtend.ps1` e `remove.ps1` (in inglese)

**Files:**
- Create: `tools/install-app.ps1`
- Modify: `Start-FreeExtend.ps1`, `tools/virtual-display/remove.ps1`

**Interfaces:**
- Produces: `tools\install-app.ps1 [-Force]`: dot-source di `tools\env.ps1`; se il pacchetto `it.freeextend.display` e gia sul tablet e non c'e `-Force` stampa `App already installed.` ed esce 0; altrimenti costruisce l'APK (stessa logica di oggi: `android\local.properties` da `%LOCALAPPDATA%\Android\Sdk`, `gradlew.bat assembleDebug`) e fa `adb install -r`; ogni errore e un `throw` (exit != 0).
- Produces: `remove.ps1` che funziona anche senza `vdd-download\`: se `devcon.exe` manca esegue prima `get-driver.ps1`; dopo la rimozione cancella `C:\VirtualDisplayDriver` se esiste (cartella creata da `install.ps1`).

- [ ] **Step 1: Create** `tools/install-app.ps1` spostando la sezione "installa l'app" di `Start-FreeExtend.ps1`; messaggi in inglese.
- [ ] **Step 2: Modify** `Start-FreeExtend.ps1`: la sezione app diventa `& "$root\tools\install-app.ps1" -Force:$InstallApp`; tutti i messaggi, i commenti d'intestazione e l'help in inglese (parametri invariati).
- [ ] **Step 3: Modify** `remove.ps1` come da Interfaces, messaggi in inglese.
- [ ] **Step 4: Verifica sintassi**: per i tre file `[System.Management.Automation.Language.Parser]::ParseFile($f,[ref]$null,[ref]$err)` e `$err.Count -eq 0`.
- [ ] **Step 5: Prova dal vivo** (tablet collegato): `powershell -NoProfile -File .\tools\install-app.ps1` stampa `App already installed.` e esce 0; `powershell -NoProfile -File .\Start-FreeExtend.ps1 -Seconds 5 -Yes` esce 0 e dopo `FreeExtend.Host.exe list-outputs` mostra il monitor virtuale non attaccato. NON eseguire `remove.ps1` (distruttivo).
- [ ] **Step 6: Commit**: `git add -A && git commit -m "feat(scripts): install-app.ps1, robust remove.ps1, English messages"`

---

### Task 4: Progetto GUI e logica pura (percorsi, stato, messaggi)

**Files:**
- Create: `host/src/FreeExtend.Gui/FreeExtend.Gui.csproj`, `ProjectPaths.cs`, `PowerShellQuoting.cs`, `AdbOutput.cs`, `StatusEvaluator.cs`, `HostExitMessages.cs`, `Program.cs` (provvisorio: `Main` vuoto, sostituito nel Task 6)
- Create: `host/tests/FreeExtend.Gui.Tests/FreeExtend.Gui.Tests.csproj` (come `FreeExtend.Host.Tests.csproj`, riferimento a `FreeExtend.Gui`), test per ogni tipo
- Modify: `host/FreeExtend.sln` (`dotnet sln host\FreeExtend.sln add` dei due progetti, cartelle `src` e `tests`)

**Interfaces** (namespace `FreeExtend.Gui`, tutto `internal`, `InternalsVisibleTo FreeExtend.Gui.Tests`):
```csharp
internal sealed record ProjectPaths(string Root) {
    public string HostExe { get; }            // host\src\FreeExtend.Host\bin\Debug\net8.0-windows\FreeExtend.Host.exe
    public string InstallDriverScript { get; } // tools\virtual-display\install.ps1
    public string RemoveDriverScript { get; }  // tools\virtual-display\remove.ps1
    public string InstallAppScript { get; }    // tools\install-app.ps1
    public static string? FindRoot(string startDir, Func<string,bool> fileExists); // sale fino alla cartella con Start-FreeExtend.ps1
}
internal static class PowerShellQuoting { public static string Quote(string s); }   // 'abc' con ' raddoppiato
internal static class AdbOutput {
    public static bool IsPackageInstalled(string pmListOutput, string package);    // riga esatta "package:<package>"
    public static string? ParseModel(string getpropOutput);                         // Trim, vuoto => null
}
internal enum TabletState { NoAdb, None, Unauthorized, Ready }
internal enum CheckState { Ok, Missing, Unknown, Warning }
internal enum RequirementId { Driver, HostProgram, Gpu, GpuPreference, Tablet, TabletApp }
internal sealed record SystemSnapshot(bool DriverInstalled, bool HostExeExists, TabletState Tablet, string? TabletModel,
    bool? AppInstalled, IReadOnlyList<string> Gpus, bool? HardwareEncoderFound, bool GpuPreferenceSet);
internal sealed record RequirementRow(RequirementId Id, CheckState State, string Text, string? ActionLabel);
internal static class StatusEvaluator {
    public static IReadOnlyList<RequirementRow> Evaluate(SystemSnapshot s);
    public static string? WhyCannotStart(SystemSnapshot s);   // null = si puo avviare
}
internal static class HostExitMessages { public static string Describe(int exitCode, string? lastErrorLine); }
```
Regole di `Evaluate` (testi in inglese; i test controllano `State`, `ActionLabel` e una sottostringa chiave):
- Driver: Ok `Virtual display driver installed` / Missing `Virtual display driver not installed`, azione `Install`.
- HostProgram: Ok `FreeExtend host program ready` / Missing `Host program not built: run Start-FreeExtend-Gui.ps1`, nessuna azione.
- Gpu: nessuna scheda => Missing `No graphics adapter found`; `HardwareEncoderFound == null` => Unknown `Checking the video encoder...`; true => Ok `Graphics: <nomi separati da ", "> (hardware H.264 encoder found)`; false => Missing `No hardware H.264 encoder found: needs an NVIDIA, AMD or Intel GPU`. Nessuna azione.
- GpuPreference: riga presente solo se `Gpus.Count >= 2`; Ok `High-performance GPU preference set` / Warning `Dual-GPU laptop: GPU preference not set for FreeExtend.Host.exe`, azione `Set`.
- Tablet: `NoAdb` Missing `adb not found: install Android Studio or set ANDROID_HOME`; `None` Missing `No tablet connected: plug in the USB-C cable and enable USB debugging`; `Unauthorized` Warning `Tablet not authorized: accept "Allow USB debugging" on the tablet`; `Ready` Ok `Tablet connected: <modello>` (senza modello solo `Tablet connected`). Nessuna azione.
- TabletApp: `AppInstalled == null` Unknown `Connect the tablet to check the app`; true Ok `FreeExtend app installed on the tablet`; false Missing `FreeExtend app not installed on the tablet`, azione `Install`.

`WhyCannotStart` restituisce il primo che si applica, in quest'ordine: driver mancante, programma mancante, nessuna scheda o encoder assente (o ancora in controllo: `Still checking the video encoder`), tablet non `Ready` (messaggio uguale al testo della riga), app non installata. La preferenza grafica mancante NON blocca.
`HostExitMessages.Describe`: 0 => `Stopped`; 1 => `Streaming failed` (+ `: <ultima riga>` se presente); 2 => `Virtual display driver not installed or no display modes configured`; 3 => `Tablet not connected or not authorized`; 4 => `No usable hardware H.264 encoder` (+ ultima riga); altro => `Host stopped unexpectedly (code N)`.

- [ ] **Step 1: Create** i due progetti (`UseWindowsForms` true, `OutputType` WinExe, `AssemblyName` `FreeExtend`, `ApplicationHighDpiMode` `SystemAware`, `ProjectReference` a `FreeExtend.Host`) e aggiungerli alla soluzione; `dotnet build host\FreeExtend.sln -v q` deve passare.
- [ ] **Step 2: Write the failing tests**: `ProjectPathsTests` (`FindRoot` trova la cartella con `Start-FreeExtend.ps1` risalendo da una sottocartella finta; `null` se non c'e; `HostExe` termina con `host\src\FreeExtend.Host\bin\Debug\net8.0-windows\FreeExtend.Host.exe` e gestisce una radice con spazi), `PowerShellQuotingTests` (`C:\a b\x'y.ps1` => `'C:\a b\x''y.ps1'`), `AdbOutputTests` (`package:it.freeextend.display` vero; `package:it.freeextend.display2` falso; output vuoto falso; `ParseModel("SM-X520\r\n")` => `"SM-X520"`, `"\n"` => null), `StatusEvaluatorTests` (una riga per ogni regola sopra, riga `GpuPreference` assente con una sola scheda, `WhyCannotStart` null con tutto a posto e per ogni caso l'ordine di priorita, preferenza grafica mancante non blocca), `HostExitMessagesTests` (i codici 0-4 e uno sconosciuto).
- [ ] **Step 3: Run to verify failure**: `dotnet test host\tests\FreeExtend.Gui.Tests`; atteso: FAIL (tipi mancanti).
- [ ] **Step 4: Implement** i tipi come da Interfaces.
- [ ] **Step 5: Run to verify pass**: `dotnet test host\FreeExtend.sln`; atteso: tutto verde.
- [ ] **Step 6: Commit**: `git add -A && git commit -m "feat(gui): project, paths, status evaluator and exit-code messages"`

---

### Task 5: Servizi della GUI (sonda di sistema, sessione di trasmissione, script, disinstallazione)

**Files:**
- Create: `host/src/FreeExtend.Gui/SystemProbe.cs`, `StreamSession.cs`, `ScriptRunner.cs`, `Uninstaller.cs`
- Test: `host/tests/FreeExtend.Gui.Tests/SystemProbeTests.cs`

**Interfaces:**
- Consumes: `AdbTunnel.ParseDevices`, `AdbTunnel.Run`, `GpuAdapters.ListNames`, `GpuPreference`, `VirtualOutputFinder.Find`, `HostStatusLine.TryParse`, `HostStatus`, tipi del Task 4.
- Produces:
  ```csharp
  internal sealed class SystemProbe {
      public SystemProbe(ProjectPaths paths, Func<string,int,(int ExitCode,string Output)> runAdb); // in produzione: AdbTunnel.Run
      public Task<SystemSnapshot> CaptureAsync();                          // tutto, fuori dal thread UI
      public Task<(TabletState State, string? Model)> TabletOnlyAsync();   // solo adb devices (+ modello se Ready)
  }
  ```
  `Win32Exception` o `FileNotFoundException` da `runAdb` => `NoAdb`; `TimeoutException` => `None`; `unauthorized` => `Unauthorized`. `AppInstalled` e `null` se il tablet non e `Ready`. `HardwareEncoderFound` = exit code 0 di `FreeExtend.Host.exe encoder-probe` (timeout 20 s; `null` se l'exe manca). `GpuPreferenceSet` = `new GpuPreference().IsHighPerformance(paths.HostExe)`.
  ```csharp
  internal sealed class StreamSession : IDisposable {
      public StreamSession(string hostExe);
      public event Action<HostStatus>? Status;
      public event Action<string>? Log;                 // righe di stdout/stderr che non sono righe di stato
      public event Action<int, string?>? Exited;        // exit code, ultima riga di stderr
      public bool IsRunning { get; }
      public void Start();                               // args: stream --status --stop-on-stdin; nascosto, stdin/out/err rediretti
      public Task StopAsync(TimeSpan timeout);           // scrive "stop"; se non esce entro timeout: Kill(entireProcessTree: true)
  }
  internal sealed record ScriptResult(int ExitCode, bool Cancelled, string LogTail);
  internal static class ScriptRunner {
      public static Task<ScriptResult> RunAsync(string scriptPath, bool elevated, params string[] args);
  }
  internal sealed record UninstallReport(bool DriverRemoved, bool GpuPreferenceRemoved, bool? AppRemoved, string? Error);
  internal static class Uninstaller {
      public static Task<UninstallReport> RunAsync(ProjectPaths paths, bool tabletReady);
  }
  ```
  `ScriptRunner`: `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& <Quote(script)> <args quotati> *>&1 | Out-File -Encoding utf8 <Quote(log)>; exit $LASTEXITCODE"`, `UseShellExecute = true`, `Verb = "runas"` solo se `elevated`, finestra nascosta; `Win32Exception` con `NativeErrorCode == 1223` (UAC rifiutato) => `Cancelled = true`; `LogTail` = ultime 20 righe del log (file temporaneo cancellato dopo). `Uninstaller`: nell'ordine `remove.ps1` elevato, `GpuPreference().Remove(paths.HostExe)`, `adb uninstall it.freeextend.display` solo se `tabletReady` (altrimenti `AppRemoved = null`); un passo fallito non ferma gli altri.

- [ ] **Step 1: Write the failing tests** `SystemProbeTests` con un `runAdb` finto: `runAdb` che lancia `Win32Exception` => `Tablet == NoAdb`; che lancia `TimeoutException` => `None`; output `List of devices attached\nXYZ\tunauthorized` => `Unauthorized`; `...\tdevice` con `getprop` che risponde `SM-X520` e `pm list packages` vuoto => `Ready`, modello `SM-X520`, `AppInstalled == false`; con `package:it.freeextend.display` => `true`; senza tablet `AppInstalled == null`.
- [ ] **Step 2: Run to verify failure**: `dotnet test host\tests\FreeExtend.Gui.Tests --filter SystemProbe`; atteso: FAIL.
- [ ] **Step 3: Implement** `SystemProbe`, poi `StreamSession`, `ScriptRunner`, `Uninstaller` come da Interfaces. Gli eventi di `StreamSession` arrivano da thread del pool (la finestra li riporta sul thread UI nel Task 6). Il processo figlio non deve mostrare console (`CreateNoWindow = true`).
- [ ] **Step 4: Run to verify pass**: `dotnet test host\FreeExtend.sln`; atteso: tutto verde.
- [ ] **Step 5: Prova dal vivo di `StreamSession`** con un piccolo script C# o PowerShell nello scratchpad che carica `FreeExtend.dll` (la GUI compilata) e avvia/ferma una sessione con `--no-adb` sotto controllo: atteso `Status(Waiting)` poi stop con exit 0 e monitor non attaccato. (Se scomodo da caricare, rimandare alla prova del Task 6 e annotarlo.)
- [ ] **Step 6: Commit**: `git add -A && git commit -m "feat(gui): system probe, stream session, script runner, uninstaller"`

---

### Task 6: Finestre, avvio e istanza unica

**Files:**
- Create: `host/src/FreeExtend.Gui/MainForm.cs`, `SettingsForm.cs`, `Start-FreeExtend-Gui.ps1` (nella radice del progetto)
- Modify: `host/src/FreeExtend.Gui/Program.cs`

**Interfaces:**
- Consumes: tutto dei Task 4-5.
- Produces: `Program.Main` con `Mutex("Local\\FreeExtend.Gui")`: se gia preso mostra `MessageBox` `FreeExtend is already running.` ed esce; altrimenti `ApplicationConfiguration.Initialize()` e `Application.Run(new MainForm(paths))` con `ProjectPaths.FindRoot(AppContext.BaseDirectory, File.Exists)` (se `null`: `MessageBox` `Could not find the FreeExtend folder (Start-FreeExtend.ps1).` ed esci).
- Produces: `Start-FreeExtend-Gui.ps1 [-Rebuild]`: se `host\src\FreeExtend.Gui\bin\Debug\net8.0-windows\FreeExtend.exe` manca o c'e `-Rebuild` esegue `dotnet build host\src\FreeExtend.Gui -c Debug --nologo -v q`; poi `Start-Process` dell'exe senza attendere.

Comportamento della finestra (fissa, ~460 px di larghezza, titolo `FreeExtend`):
- Una riga per ogni `RequirementRow` di `StatusEvaluator.Evaluate` (simbolo: Ok verde, Missing rosso, Warning arancio, Unknown grigio; testo; pulsante con `ActionLabel` se presente), un pulsante grande `Start streaming` / `Stop streaming`, una riga di stato in basso, un pulsante `Settings`.
- Aperto: `SystemProbe.CaptureAsync()`; poi ogni 3 s `TabletOnlyAsync()` (solo se nessuna azione ne trasmissione in corso). Tutte le chiamate adb in `Task.Run`, risultati riportati sul thread UI.
- `Start streaming` abilitato solo se `WhyCannotStart == null`; altrimenti disabilitato con il motivo come suggerimento/riga di stato. Avvio: `StreamSession.Start()`; righe di stato: `Waiting for the tablet...` (waiting/disconnected), `Streaming <W>x<H>@<Hz>, <fps> fps` (connected/fps). Fine del processo: pulsante torna `Start streaming`, riga di stato = `HostExitMessages.Describe(...)`, rilettura completa.
- Azioni: `Install` (driver) = `ScriptRunner.RunAsync(InstallDriverScript, elevated: true)`; `Install` (app) = `RunAsync(InstallAppScript, elevated: false)`; `Set` = `new GpuPreference().SetHighPerformance(HostExe)` (nessuna elevazione). Durante un'azione tutti i pulsanti sono disabilitati e la riga di stato dice cosa sta facendo; a fine azione rilettura completa; `Cancelled` => `Cancelled` senza errore; errore => ultime righe del log in un `MessageBox`.
- `FormClosing`: se sta trasmettendo annulla la chiusura, mostra `Stopping...`, `await StopAsync(10 s)` e poi chiude.
- `SettingsForm` (modale): un solo pulsante `Uninstall everything...`. Conferma con `MessageBox` YesNo che elenca: driver del monitor virtuale (richiede amministratore), preferenza grafica per `FreeExtend.Host.exe`, app sul tablet (solo se collegato); precisa che la cartella del progetto resta. Se sta trasmettendo prima ferma la trasmissione. Poi `Uninstaller.RunAsync` e un riepilogo (`MessageBox`) con l'esito di ogni voce; se il driver e stato rimosso aggiunge `Restart Windows if a ghost monitor remains.`

- [ ] **Step 1: Implement** `Program`, `MainForm`, `SettingsForm` e il launcher come da Interfaces.
- [ ] **Step 2: Build**: `dotnet build host\FreeExtend.sln -v q`; atteso: 0 errori.
- [ ] **Step 3: Prova dal vivo** (tablet collegato, accettato il debug USB): `powershell -NoProfile -File .\Start-FreeExtend-Gui.ps1`; screenshot della sola finestra (ritagliato, poi cancellato): righe tutte Ok, pulsante `Start streaming` attivo. Cliccare `Start streaming` (o inviare il click con automazione UI se disponibile; altrimenti chiedere all'utente), aprire l'app sul tablet: il tablet mostra il desktop e la finestra dice `Streaming ...`.
- [ ] **Step 4: Prova istanza unica**: lanciare `Start-FreeExtend-Gui.ps1` una seconda volta; atteso: messaggio `FreeExtend is already running.` e nessuna seconda finestra.
- [ ] **Step 5: Prova stop e chiusura**: `Stop streaming` => `FreeExtend.Host.exe list-outputs` mostra il monitor non attaccato; poi riavviare la trasmissione e chiudere la finestra con la X: attende, il monitor risulta staccato e nessun `FreeExtend.Host.exe` resta in esecuzione (`Get-Process FreeExtend.Host`).
- [ ] **Step 6: Commit**: `git add -A && git commit -m "feat(gui): main window, settings, single instance, launcher"`

---

### Task 7: Traduzione in inglese del programma sul PC e del protocollo

**Files:**
- Modify: tutti i `.cs` sotto `host/src/FreeExtend.Host` e `host/src/FreeExtend.Protocol` che contengono testo visibile (output `Console.*`, messaggi di eccezione, uso/help): in particolare `Program.cs`, `StreamCommand.cs`, `SetupGpu.cs`, `Net/AdbTunnel.cs`, `Net/StreamServer.cs`, `Net/TunnelTest.cs`, `Display/*.cs`, `Encode/*.cs`, `CaptureTest.cs`, `Capture/*.cs`, `MessageCodec.cs`, `Payloads.cs`
- Test: eventuali test che asseriscono su testo tradotto (verificare con la ricerca)

- [ ] **Step 1: Individuare** le stringhe: `grep -rnE "Console\.(Write|Error)|throw new|Exception\(" host/src --include=*.cs` (circa 90 righe) ed escludere i commenti.
- [ ] **Step 2: Tradurre** solo il testo mostrato all'utente o nelle eccezioni (non commenti, non identificatori, non il formato delle righe `@@fx`). Mantenere le parole chiave che i test cercano (`payload` in `MessageCodec`) e il prefisso `ERROR:` al posto di `ERRORE:`.
- [ ] **Step 3: Verificare** che non restino stringhe italiane: `grep -rnE "\"[^\"]*(\bnon\b|\bdella\b|\bil tablet\b|[àèéìòù])[^\"]*\"" host/src --include=*.cs` non deve trovare nulla (controllare a occhio i falsi positivi).
- [ ] **Step 4: Run tests**: `dotnet test host\FreeExtend.sln`; atteso: tutto verde (correggere qui solo i test che asserivano su testo tradotto).
- [ ] **Step 5: Commit**: `git commit -am "i18n(host): English console output and error messages"`

---

### Task 8: Traduzione in inglese degli script restanti

**Files:**
- Modify: `tools/env.ps1`, `tools/virtual-display/check.ps1`, `get-driver.ps1`, `install.ps1`, `vdd-pipe.ps1`, `tools/encode-bench.ps1`, `tools/stage0-measure.ps1`, `tools/motion-window.ps1`

- [ ] **Step 1: Tradurre** messaggi (`Write-Host`, `throw`, `Write-Error`) e l'help d'intestazione; non cambiare parametri, nomi di funzione o logica. Per `check.ps1` il testo `modo ...: OK / NON impostato` diventa `mode ...: OK / NOT set`.
- [ ] **Step 2: Verifica sintassi** di tutti i file con `Parser.ParseFile` (zero errori).
- [ ] **Step 3: Prova dal vivo**: `powershell -NoProfile -File .\tools\virtual-display\check.ps1` e `powershell -NoProfile -File .\tools\virtual-display\install.ps1 -ConfigOnly -OutFile <scratchpad>\vdd.xml` (nessuna installazione); attesi: output in inglese, exit code come prima (0 con il monitor attaccato al desktop, altrimenti 1 con messaggio `... NOT set`).
- [ ] **Step 4: Verifica** che non restino frasi italiane: `grep -rnE "[àèéìòù]|\b(Scarica|Installa|Rimozione|Serve|Nessuna)\b" tools Start-FreeExtend*.ps1` senza risultati (commenti inclusi nei file `.ps1` modificati: qui si traducono anche perche sono l'help).
- [ ] **Step 5: Commit**: `git commit -am "i18n(scripts): English messages and help"`

---

### Task 9: Testi dell'app Android in inglese

**Files:**
- Modify: `android/app/src/main/java/it/freeextend/display/MainActivity.kt` (righe 38, 106, 112, 129, 142)

- [ ] **Step 1: Tradurre** le stringhe: `FreeExtend\nIn attesa del PC...` => `FreeExtend\nWaiting for the PC...` (righe 38 e 142), `Decoder non avviato\n...` => `Decoder failed to start\n...`, `Connesso ...` => `Connected ...`, `FreeExtend\nRiconnessione...` => `FreeExtend\nReconnecting...`.
- [ ] **Step 2: Run tests**: `cd android; .\gradlew.bat testDebugUnitTest`; atteso: 34 test verdi.
- [ ] **Step 3: Reinstallare e controllare sul tablet**: `powershell -NoProfile -File .\tools\install-app.ps1 -Force`, aprire l'app, schermata del tablet con `adb exec-out screencap -p` in una cartella dello scratchpad, ritagliata alla sola riga di testo, guardarla e cancellarla. Atteso: `Waiting for the PC...`.
- [ ] **Step 4: Commit**: `git commit -am "i18n(android): English on-screen texts"`

---

### Task 10: README in inglese (con la finestra)

**Files:**
- Modify: `README.md`, `tools/virtual-display/README.md`

- [ ] **Step 1: Riscrivere** `README.md` in inglese mantenendo struttura e contenuto (stato, misure, requisiti, installazione driver, avvio, compatibilita, due schede grafiche, risoluzione problemi, sicurezza, sviluppo, licenze) e aggiungere una sezione per la finestra: `powershell -NoProfile -File .\Start-FreeExtend-Gui.ps1` come modo principale (stato dei requisiti, Start/Stop, Settings > Uninstall everything), con gli script da riga di comando come alternativa. Aggiornare i testi che citano messaggi tradotti (es. `ERROR: No usable hardware H.264 encoder...`) e il clone URL resta `https://github.com/AntoLupoli/FreeExtendDesk.git`.
- [ ] **Step 2: Riscrivere** `tools/virtual-display/README.md` in inglese.
- [ ] **Step 3: Verificare**: ogni comando e opzione citata esiste (`Start-FreeExtend.ps1 -KeepDisplay -InstallApp -Yes -Seconds -Csv`, `Start-FreeExtend-Gui.ps1 -Rebuild`, `install.ps1 -Gpu -ExtraModes -ConfigOnly`, `FreeExtend.Host.exe setup-gpu|gpu-status|display-test detach`); nessun testo italiano: `grep -nE "[àèéìòù]" README.md tools/virtual-display/README.md` senza risultati.
- [ ] **Step 4: Commit**: `git commit -am "docs: English README with the GUI section"`

---

### Task 11: Verifica finale

**Files:** nessuno (solo prove; eventuali correzioni in commit a parte)

- [ ] **Step 1: Test completi**: `dotnet test host\FreeExtend.sln` e `cd android; .\gradlew.bat testDebugUnitTest`; atteso: tutto verde.
- [ ] **Step 2: Build pulita**: `dotnet build host\FreeExtend.sln -c Debug --nologo -v q` senza errori o avvisi nuovi.
- [ ] **Step 3: Prova completa dal vivo** (tablet collegato): aprire la finestra, avviare la trasmissione, verificare l'immagine sul tablet, fermare; poi avviarla di nuovo e **terminare bruscamente la finestra** (`Stop-Process -Name FreeExtend -Force`): entro 10 s nessun `FreeExtend.Host` in esecuzione e il monitor virtuale e staccato (`list-outputs`). Chiedere all'utente di cliccare `No` su una richiesta UAC (azione `Install` driver non necessaria: usare `Settings > Uninstall everything...` e rispondere `No` alla conferma, poi provare `Yes` e `No` a UAC solo con il suo consenso esplicito) per verificare che "annullato" non sia un errore.
- [ ] **Step 4: Controllo dati personali**: `git grep -iE "gmail|macio|serial" ` sull'albero non deve trovare nulla di nuovo; cancellare gli screenshot residui dallo scratchpad.
- [ ] **Step 5: Aggiornare la memoria** `freeextend-project.md` (esiste la GUI, si lancia con `Start-FreeExtend-Gui.ps1`; interfaccia in inglese; la disinstallazione completa non e stata provata dal vivo se l'utente non ha dato il consenso).
- [ ] **Step 6: Commit** di eventuali correzioni: `git commit -am "fix: issues found in final verification"` (solo se servono).
