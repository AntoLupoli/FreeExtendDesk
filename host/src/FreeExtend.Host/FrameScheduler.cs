namespace FreeExtend.Host;

/// <summary>
/// Decide quando ricodificare l'ultima immagine catturata. Windows consegna un frame solo quando il desktop cambia: se l'ultimo
/// cambiamento prima di una pausa non viene codificato (encoder occupato, frame troppo ravvicinato) Windows non lo riconsegna, e
/// il tablet resterebbe sull'immagine precedente per tutto il tempo che il desktop sta fermo. Qui si tiene traccia di "l'immagine
/// piu recente non e ancora stata mandata" (<see cref="Dirty"/>) e di una richiesta di keyframe in sospeso, e si ricodifica
/// l'ultima immagine appena il desktop e fermo da <see cref="IdleMs"/>.
/// </summary>
public sealed class FrameScheduler
{
    /// <summary>Dopo questo tempo senza frame nuovi il desktop e considerato fermo (circa un frame a 60 Hz).</summary>
    public const long IdleMs = 20;

    private long _lastCaptureMs;
    private bool _hasCapture;

    /// <summary>L'ultima cattura e piu recente dell'ultima immagine codificata.</summary>
    public bool Dirty { get; private set; }

    /// <summary>Serve un keyframe (tablet nuovo, frame scartato, richiesta del tablet) e non e ancora stato codificato.</summary>
    public bool PendingKeyframe { get; private set; }

    public void RequestKeyframe() => PendingKeyframe = true;

    /// <summary>Windows ha consegnato un frame nuovo (copiato nella texture dell'ultima cattura).</summary>
    public void OnCaptured(long nowMs)
    {
        _lastCaptureMs = nowMs;
        _hasCapture = true;
        Dirty = true;
    }

    /// <summary>L'immagine attuale e stata codificata (con il keyframe forzato se ce n'era uno in sospeso).</summary>
    public void OnEncoded()
    {
        Dirty = false;
        PendingKeyframe = false;
    }

    /// <summary>La pipeline e stata ricostruita: la texture dell'ultima cattura non esiste piu.</summary>
    public void Reset()
    {
        _hasCapture = false;
        Dirty = false;
    }

    /// <summary>Vero se conviene ricodificare ora l'ultima immagine: tablet connesso, encoder con uno slot libero, qualcosa da mandare e desktop fermo.</summary>
    public bool ShouldReencodeLast(long nowMs, bool connected, bool encoderWants) =>
        connected && encoderWants && _hasCapture && (Dirty || PendingKeyframe) && nowMs - _lastCaptureMs >= IdleMs;
}
