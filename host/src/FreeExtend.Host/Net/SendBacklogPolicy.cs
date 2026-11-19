namespace FreeExtend.Host.Net;

public enum BacklogDecision { Send, DropAndRequestKeyframe }

/// <summary>
/// Decide se un pacchetto video va accodato o scartato quando il tablet (o il cavo) e in ritardo. Regole:
/// i keyframe si inviano sempre; un frame non-keyframe con <see cref="MaxQueued"/> o piu pacchetti gia in coda si scarta;
/// dopo uno scarto si scartano anche tutti i frame successivi fino al prossimo keyframe, perche un P-frame dopo un frame
/// perso romperebbe la catena di decodifica. La latenza non deve mai crescere accumulando frame.
/// </summary>
public sealed class SendBacklogPolicy
{
    public const int MaxQueued = 2;

    private bool _waitingForKeyframe;

    /// <param name="startWaitingForKeyframe">Vero per un client appena connesso: senza un frame di riferimento i frame non-keyframe non servono.</param>
    public SendBacklogPolicy(bool startWaitingForKeyframe = false) => _waitingForKeyframe = startWaitingForKeyframe;

    public BacklogDecision OnPacket(int queuedPackets, bool isKeyframe)
    {
        if (isKeyframe)
        {
            _waitingForKeyframe = false;
            return BacklogDecision.Send;
        }
        if (_waitingForKeyframe || queuedPackets >= MaxQueued)
        {
            _waitingForKeyframe = true;
            return BacklogDecision.DropAndRequestKeyframe;
        }
        return BacklogDecision.Send;
    }
}
