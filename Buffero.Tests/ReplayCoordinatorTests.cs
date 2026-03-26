using Buffero.App.Infrastructure;
using Buffero.Core.Capture;

namespace Buffero.Tests;

public sealed class ReplayCoordinatorTests
{
    [Fact]
    public void GetFinalizedSegments_DropsNewestSegment()
    {
        var finalized = ReplayCoordinator.GetFinalizedSegments(
        [
            new SegmentInfo("1.mp4", 1, 100, DateTimeOffset.UtcNow.AddSeconds(-6)),
            new SegmentInfo("2.mp4", 2, 100, DateTimeOffset.UtcNow.AddSeconds(-4)),
            new SegmentInfo("3.mp4", 3, 100, DateTimeOffset.UtcNow.AddSeconds(-2))
        ]);

        Assert.Equal([1, 2], finalized.Select(segment => segment.Sequence));
    }

    [Fact]
    public void GetFinalizedSegments_ReturnsEmpty_WhenOnlyActiveSegmentExists()
    {
        var finalized = ReplayCoordinator.GetFinalizedSegments(
        [
            new SegmentInfo("1.mp4", 1, 100, DateTimeOffset.UtcNow.AddSeconds(-2))
        ]);

        Assert.Empty(finalized);
    }
}
