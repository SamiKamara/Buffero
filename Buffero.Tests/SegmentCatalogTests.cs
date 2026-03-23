using Buffero.Core.Capture;

namespace Buffero.Tests;

public sealed class SegmentCatalogTests
{
    [Fact]
    public void GetReplaySnapshot_ReturnsNewestSegmentsForWindow()
    {
        var catalog = new SegmentCatalog(segmentSeconds: 2);
        catalog.ReplaceSegments(
        [
            new SegmentInfo("1.mp4", 1, 100, DateTimeOffset.UtcNow.AddSeconds(-8)),
            new SegmentInfo("2.mp4", 2, 100, DateTimeOffset.UtcNow.AddSeconds(-6)),
            new SegmentInfo("3.mp4", 3, 100, DateTimeOffset.UtcNow.AddSeconds(-4)),
            new SegmentInfo("4.mp4", 4, 100, DateTimeOffset.UtcNow.AddSeconds(-2))
        ]);

        var snapshot = catalog.GetReplaySnapshot(5);

        Assert.Equal([2, 3, 4], snapshot.Select(segment => segment.Sequence));
    }

    [Fact]
    public void GetOverflowSegments_ReturnsOldestSegmentsBeyondRetention()
    {
        var catalog = new SegmentCatalog(segmentSeconds: 2);
        catalog.ReplaceSegments(
        [
            new SegmentInfo("1.mp4", 1, 100, DateTimeOffset.UtcNow.AddSeconds(-10)),
            new SegmentInfo("2.mp4", 2, 100, DateTimeOffset.UtcNow.AddSeconds(-8)),
            new SegmentInfo("3.mp4", 3, 100, DateTimeOffset.UtcNow.AddSeconds(-6)),
            new SegmentInfo("4.mp4", 4, 100, DateTimeOffset.UtcNow.AddSeconds(-4)),
            new SegmentInfo("5.mp4", 5, 100, DateTimeOffset.UtcNow.AddSeconds(-2))
        ]);

        var overflow = catalog.GetOverflowSegments(bufferSeconds: 4, maxBytes: 10_000);

        Assert.Equal([1], overflow.Select(segment => segment.Sequence));
    }
}
