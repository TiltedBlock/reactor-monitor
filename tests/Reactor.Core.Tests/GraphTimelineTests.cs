using Reactor.Core.Telemetry;
using Xunit;

namespace Reactor.Core.Tests;

public sealed class GraphTimelineTests
{
    [Fact]
    public void ReviewKeepsItsTimestampWhileLiveSamplesArrive()
    {
        var timeline = new GraphTimeline(120);
        for (int i = 1; i <= 300; i++) timeline.Append(Sample(i));

        timeline.Scroll(-60);
        timeline.Append(Sample(301));

        Assert.False(timeline.IsLive);
        Assert.Equal(240, timeline.End);
        Assert.Equal(240, timeline.VisibleSamples().Last().Seconds);
        timeline.GoLive();
        Assert.Equal(301, timeline.End);
    }

    [Fact]
    public void RetentionClampsReviewAndResetReturnsToLive()
    {
        var timeline = new GraphTimeline(120);
        for (int i = 1; i <= 2000; i++) timeline.Append(Sample(i));
        timeline.Scroll(-3000);
        Assert.Equal(200, timeline.VisibleSamples().First().Seconds);

        timeline.Append(Sample(2010));
        Assert.Equal(210, timeline.VisibleSamples().First().Seconds);
        timeline.Clear();
        Assert.True(timeline.IsLive);
        Assert.Empty(timeline.VisibleSamples());
    }

    [Fact]
    public void SleepGapKeepsItsRealDurationWithoutAddingReadings()
    {
        var timeline = new GraphTimeline(120);
        timeline.Append(Sample(10));
        timeline.Append(Sample(100) with { BreakBefore = true });

        var samples = timeline.VisibleSamples().ToArray();
        Assert.Equal(2, samples.Length);
        Assert.Equal(90, samples[1].Seconds - samples[0].Seconds);
        Assert.True(samples[1].BreakBefore);
    }

    private static GraphSample Sample(double time) => new(time, 10, 40, 20, 45, 80);
}
