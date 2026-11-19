# Sends a command to the Virtual Display Driver's control named pipe (\\.\pipe\MTTVirtualDisplayPipe).
# Commands (from the driver source Driver.cpp, HandleClient): RELOAD_DRIVER | SETGPU "card name" | SETDISPLAYCOUNT n |
#   HARDWARECURSOR true|false | GETSETTINGS | PING | GETASSIGNEDGPU | GETALLGPUS (the last ones write to the log).
# The pipe has an "Everyone" ACL: no administrator rights needed. Each connection handles a single message (UTF-16).
# Usage: powershell -NoProfile -File tools\virtual-display\vdd-pipe.ps1 -Command 'SETGPU "AMD Radeon(TM) Graphics"'
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
    Write-Host "Sent: $Command"
} finally {
    $pipe.Dispose()
}
