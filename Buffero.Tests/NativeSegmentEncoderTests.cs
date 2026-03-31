using Buffero.App.Infrastructure;

namespace Buffero.Tests;

public sealed class NativeSegmentEncoderTests
{
    [Fact]
    public void GetTargetFrameCount_ReturnsExpectedFrameTotal()
    {
        var frameCount = NativeSegmentEncoder.GetTargetFrameCount(TimeSpan.FromSeconds(2), 30);

        Assert.Equal(60, frameCount);
    }

    [Fact]
    public void GetSampleTime_UsesConstantFramePacing()
    {
        var sampleTime = NativeSegmentEncoder.GetSampleTime(59, 30);

        Assert.Equal(TimeSpan.FromSeconds(59d / 30d), sampleTime);
    }
}
