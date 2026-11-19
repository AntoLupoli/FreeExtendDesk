# Invia un comando alla named pipe di controllo del Virtual Display Driver (\\.\pipe\MTTVirtualDisplayPipe).
# Comandi (dal sorgente Driver.cpp, HandleClient): RELOAD_DRIVER | SETGPU "nome scheda" | SETDISPLAYCOUNT n |
#   HARDWARECURSOR true|false | GETSETTINGS | PING | GETASSIGNEDGPU | GETALLGPUS (questi ultimi scrivono nel log).
# La pipe ha ACL "Everyone": non servono permessi di amministratore. Ogni connessione elabora un solo messaggio (UTF-16).
# Uso: powershell -NoProfile -File tools\virtual-display\vdd-pipe.ps1 -Command 'SETGPU "AMD Radeon(TM) Graphics"'
param([Parameter(Mandatory)][string]$Command, [switch]$ReadReply)
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'MTTVirtualDisplayPipe', [System.IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(3000)
    $bytes = [System.Text.Encoding]::Unicode.GetBytes($Command)
    $pipe.Write($bytes, 0, $bytes.Length)
    $pipe.Flush()
    if ($ReadReply) {
        $buf = New-Object byte[] 512
        $n = $pipe.Read($buf, 0, $buf.Length)
        if ($n -gt 0) { [System.Text.Encoding]::Unicode.GetString($buf, 0, $n).TrimEnd([char]0) }
    }
    Write-Host "Inviato: $Command"
} finally {
    $pipe.Dispose()
}
