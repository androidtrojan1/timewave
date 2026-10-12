using System.IO;
using TimeWave.Core;
using TimeWave.Shared;

namespace TimeWave.Tests;

public class ProtocolTests
{
    static T RoundTrip<T>(T message) where T : Message
    {
        var bytes = Protocol.Encode(message);
        return (T)Protocol.ReadFrame(new MemoryStream(bytes))!;
    }

    [Fact]
    public void World_frame_round_trips()
    {
        var host = new EngineHost { Running = true };
        host.Update(1.0);
        var frame = host.BuildFrame(0);
        var copy = RoundTrip(frame);
        Assert.Equal(frame.PresentTick, copy.PresentTick);
        Assert.Equal(frame.Waves.Length, copy.Waves.Length);
        Assert.Equal(frame.Units.Length, copy.Units.Length);
        Assert.Equal(frame.Units[5].X, copy.Units[5].X);
        Assert.Equal(frame.Units[5].Hp, copy.Units[5].Hp);
    }

    [Fact]
    public void Stats_and_commands_round_trip()
    {
        var host = new EngineHost { Running = true };
        host.Update(5.0);
        var stats = RoundTrip(host.BuildStats());
        Assert.Equal(host.BuildStats().BucketCount, stats.BucketCount);

        var cmd = RoundTrip(new IssueCommand { Order = OrderType.Attack, UnitIds = new[] { 1, 2, 3 }, TargetId = 42 });
        Assert.Equal(new[] { 1, 2, 3 }, cmd.UnitIds);
        Assert.Equal(42, cmd.TargetId);
    }

    [Fact]
    public void Embedded_link_speaks_the_protocol()
    {
        using var link = new EmbeddedLink();
        link.Send(new Hello { Name = "tester" });
        link.Pump(0.1f);
        Message? m;
        Assert.True(link.TryReceive(out m));
        Assert.IsType<Welcome>(m);
        bool sawFrame = false;
        while (link.TryReceive(out m)) sawFrame |= m is WorldFrame;
        Assert.True(sawFrame);
    }
}
