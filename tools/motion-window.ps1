# Finestra di prova a tutto schermo sul monitor virtuale: barre in movimento, contatore in millisecondi e testo piccolo
# (per giudicare la nitidezza). Serve a generare frame continui per i test di cattura/codifica/latenza.
# Uso: powershell -NoProfile -File tools\motion-window.ps1 [-Seconds 20] [-Screen DISPLAY58]
param([int]$Seconds = 20, [string]$Screen = 'DISPLAY58')
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$target = [System.Windows.Forms.Screen]::AllScreens | Where-Object { $_.DeviceName.TrimStart('\', '.') -eq $Screen }
if (-not $target) { Write-Error "Schermo $Screen non trovato (attaccato al desktop?)"; exit 1 }

$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Bounds = $target.Bounds
$form.BackColor = [System.Drawing.Color]::Black
$form.TopMost = $true
$form.ShowInTaskbar = $false
# doppio buffer per un disegno fluido
$prop = $form.GetType().GetProperty('DoubleBuffered', [Reflection.BindingFlags]'Instance,NonPublic')
$prop.SetValue($form, $true, $null)

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$big = New-Object System.Drawing.Font('Consolas', 96, [System.Drawing.FontStyle]::Bold)
$small = New-Object System.Drawing.Font('Segoe UI', 11)
$white = [System.Drawing.Brushes]::White
$bar = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::DeepSkyBlue)

$form.Add_Paint({
    param($s, $e)
    $g = $e.Graphics
    $w = $s.ClientSize.Width; $h = $s.ClientSize.Height
    $ms = $sw.ElapsedMilliseconds
    # barra che scorre (circa 1 schermo al secondo)
    $x = [int](($ms % 1000) / 1000.0 * ($w - 120))
    $g.FillRectangle($bar, $x, 40, 120, $h - 80)
    $g.DrawString(('{0,8} ms' -f $ms), $big, $white, 40, [int]($h / 2 - 90))
    # testo piccolo per la nitidezza
    for ($i = 0; $i -lt 8; $i++) {
        $g.DrawString('The quick brown fox jumps over the lazy dog 0123456789 - FreeExtend', $small, $white, 40, $h - 240 + $i * 24)
    }
})
# Ridisegno a circa 60 fps (non a ciclo libero: a 100+ fps la finestra satura la GPU e falsa le misure).
$draw = New-Object System.Windows.Forms.Timer
$draw.Interval = 16
$draw.Add_Tick({ $form.Invalidate() })
$draw.Start()
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 250
$timer.Add_Tick({ if ($sw.Elapsed.TotalSeconds -ge $Seconds) { $form.Close() } })
$timer.Start()
[System.Windows.Forms.Application]::Run($form)
