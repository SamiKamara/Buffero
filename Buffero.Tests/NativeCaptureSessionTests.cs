using Buffero.App.Infrastructure;

namespace Buffero.Tests;

public sealed class NativeCaptureSessionTests
{
    [Fact]
    public void GetSegmentCompletionTimeout_UsesMinimumTimeout_ForShortSegments()
    {
        var timeout = NativeCaptureSession.GetSegmentCompletionTimeout(segmentSeconds: 2);

        Assert.Equal(TimeSpan.FromSeconds(10), timeout);
    }

    [Fact]
    public void GetSegmentCompletionTimeout_ScalesWithSegmentDuration_WithinBounds()
    {
        var timeout = NativeCaptureSession.GetSegmentCompletionTimeout(segmentSeconds: 5);

        Assert.Equal(TimeSpan.FromSeconds(20), timeout);
    }

    [Fact]
    public void GetSegmentCompletionTimeout_UsesMaximumTimeout_ForLongSegments()
    {
        var timeout = NativeCaptureSession.GetSegmentCompletionTimeout(segmentSeconds: 10);

        Assert.Equal(TimeSpan.FromSeconds(30), timeout);
    }
}
