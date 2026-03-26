using Buffero.App.Infrastructure;
using Buffero.Core.Configuration;

namespace Buffero.Tests;

public sealed class FfmpegCaptureSourceResolverTests
{
    [Fact]
    public void ShouldUseWindowSource_ReturnsTrue_WhenEffectiveModeIsWindowAndHandleIsValid()
    {
        var window = new CaptureTargetWindow((IntPtr)1234, "game", "Game", 0, 0, 1920, 1080);

        var result = FfmpegCaptureSourceResolver.ShouldUseWindowSource(CaptureMode.Window, window);

        Assert.True(result);
    }

    [Fact]
    public void ShouldUseWindowSource_ReturnsFalse_WhenEffectiveModeFallsBackToDisplay()
    {
        var window = new CaptureTargetWindow((IntPtr)1234, "game", "Game", 0, 0, 1920, 1080);

        var result = FfmpegCaptureSourceResolver.ShouldUseWindowSource(CaptureMode.Display, window);

        Assert.False(result);
    }

    [Fact]
    public void ShouldUseWindowSource_ReturnsFalse_WhenWindowHandleIsMissing()
    {
        var window = new CaptureTargetWindow(IntPtr.Zero, "game", "Game", 0, 0, 1920, 1080);

        var result = FfmpegCaptureSourceResolver.ShouldUseWindowSource(CaptureMode.Window, window);

        Assert.False(result);
    }

    [Fact]
    public void CreateDisplayInputFromMonitorIndex_UsesMonitorIndexSelector()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");

        var result = FfmpegCaptureSourceResolver.CreateDisplayInputFromMonitorIndex(1, settings);

        Assert.Contains("monitor_idx=1", result);
        Assert.DoesNotContain("hwnd=", result);
        Assert.DoesNotContain("hmonitor=", result);
    }

    [Fact]
    public void CreateDisplayInputFromMonitorIndex_UsesConfiguredFrameRate()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.Fps = 45;
        settings.Normalize();

        var result = FfmpegCaptureSourceResolver.CreateDisplayInputFromMonitorIndex(0, settings);

        Assert.Contains("max_framerate=45", result);
    }
}
