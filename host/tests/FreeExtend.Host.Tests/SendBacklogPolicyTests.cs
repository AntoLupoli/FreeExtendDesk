using FreeExtend.Host.Net;
using Xunit;

namespace FreeExtend.Host.Tests;

public class SendBacklogPolicyTests
{
    [Fact]
    public void Empty_queue_sends_delta_frames()
    {
        var policy = new SendBacklogPolicy();
        Assert.Equal(BacklogDecision.Send, policy.OnPacket(queuedPackets: 0, isKeyframe: false));
    }

    [Fact]
    public void Backlog_of_two_drops_delta_and_requests_keyframe()
    {
        var policy = new SendBacklogPolicy();
        Assert.Equal(BacklogDecision.DropAndRequestKeyframe, policy.OnPacket(queuedPackets: 2, isKeyframe: false));
    }

    [Fact]
    public void Keyframes_are_always_sent_even_with_a_deep_backlog()
    {
        var policy = new SendBacklogPolicy();
        Assert.Equal(BacklogDecision.Send, policy.OnPacket(queuedPackets: 5, isKeyframe: true));
    }

    [Fact]
    public void After_a_drop_every_delta_is_dropped_until_a_keyframe_even_if_the_queue_drains()
    {
        // Un P-frame dopo un frame scartato rompe la catena di decodifica: si riparte solo da un keyframe.
        var policy = new SendBacklogPolicy();
        policy.OnPacket(queuedPackets: 2, isKeyframe: false);

        Assert.Equal(BacklogDecision.DropAndRequestKeyframe, policy.OnPacket(queuedPackets: 0, isKeyframe: false));
        Assert.Equal(BacklogDecision.Send, policy.OnPacket(queuedPackets: 0, isKeyframe: true));
        Assert.Equal(BacklogDecision.Send, policy.OnPacket(queuedPackets: 0, isKeyframe: false));
    }
}

public class SendBacklogPolicyStartupTests
{
    [Fact]
    public void A_policy_that_starts_waiting_drops_deltas_until_the_first_keyframe()
    {
        // Un client appena connesso non ha nessun frame di riferimento: i P-frame prodotti prima del primo keyframe non servono.
        var policy = new SendBacklogPolicy(startWaitingForKeyframe: true);

        Assert.Equal(BacklogDecision.DropAndRequestKeyframe, policy.OnPacket(queuedPackets: 0, isKeyframe: false));
        Assert.Equal(BacklogDecision.Send, policy.OnPacket(queuedPackets: 0, isKeyframe: true));
        Assert.Equal(BacklogDecision.Send, policy.OnPacket(queuedPackets: 0, isKeyframe: false));
    }
}
