using FreeExtend.Host;
using Xunit;

namespace FreeExtend.Host.Tests;

/// <summary>
/// Regola della revisione finale (C1): a desktop fermo il tablet deve sempre mostrare l'ULTIMA immagine, anche se l'ultimo frame
/// catturato non e stato codificato perche l'encoder era occupato o il frame arrivava troppo presto.
/// </summary>
public class FrameSchedulerTests
{
    [Fact]
    public void A_captured_frame_that_was_not_encoded_is_re_encoded_once_the_desktop_goes_still()
    {
        var s = new FrameScheduler();
        s.OnCaptured(nowMs: 1000);                                    // l'ultimo cambiamento prima della quiete: non codificato

        Assert.False(s.ShouldReencodeLast(nowMs: 1005, connected: true, encoderWants: true));   // troppo presto: potrebbe arrivarne un altro
        Assert.True(s.ShouldReencodeLast(nowMs: 1025, connected: true, encoderWants: true));    // quiete: si manda l'immagine attuale
    }

    [Fact]
    public void A_frame_that_was_encoded_needs_no_re_encode()
    {
        var s = new FrameScheduler();
        s.OnCaptured(1000);
        s.OnEncoded();

        Assert.False(s.ShouldReencodeLast(nowMs: 5000, connected: true, encoderWants: true));
    }

    [Fact]
    public void A_pending_keyframe_is_sent_from_the_last_image_when_nothing_new_arrives()
    {
        var s = new FrameScheduler();
        s.OnCaptured(1000);
        s.OnEncoded();                                                // ultima immagine gia codificata...
        s.RequestKeyframe();                                          // ...ma un tablet nuovo ha bisogno di un keyframe

        Assert.True(s.PendingKeyframe);
        Assert.True(s.ShouldReencodeLast(nowMs: 1100, connected: true, encoderWants: true));
        s.OnEncoded();
        Assert.False(s.PendingKeyframe);
        Assert.False(s.ShouldReencodeLast(nowMs: 1200, connected: true, encoderWants: true));
    }

    [Fact]
    public void Nothing_is_encoded_without_a_tablet_or_while_the_encoder_has_no_free_slot()
    {
        var s = new FrameScheduler();
        s.OnCaptured(1000);

        Assert.False(s.ShouldReencodeLast(nowMs: 2000, connected: false, encoderWants: true));
        Assert.False(s.ShouldReencodeLast(nowMs: 2000, connected: true, encoderWants: false));   // si riprova al giro dopo
        Assert.True(s.ShouldReencodeLast(nowMs: 2001, connected: true, encoderWants: true));
    }

    [Fact]
    public void Without_any_captured_frame_there_is_nothing_to_re_encode()
    {
        var s = new FrameScheduler();
        s.RequestKeyframe();
        Assert.False(s.ShouldReencodeLast(nowMs: 5000, connected: true, encoderWants: true));
    }

    [Fact]
    public void Reset_forgets_the_last_frame_when_the_pipeline_is_rebuilt()
    {
        // la texture dell'ultima cattura apparteneva alla pipeline vecchia
        var s = new FrameScheduler();
        s.OnCaptured(1000);
        s.Reset();

        Assert.False(s.ShouldReencodeLast(nowMs: 5000, connected: true, encoderWants: true));
    }

    [Fact]
    public void A_new_capture_postpones_the_idle_re_encode()
    {
        var s = new FrameScheduler();
        s.OnCaptured(1000);
        s.OnCaptured(1015);                                           // un altro frame, anche questo non codificato

        Assert.False(s.ShouldReencodeLast(nowMs: 1030, connected: true, encoderWants: true));
        Assert.True(s.ShouldReencodeLast(nowMs: 1040, connected: true, encoderWants: true));
    }
}
