using Buffero.Core.Capture;
using Buffero.Core.Configuration;

namespace Buffero.Tests;

public sealed class FfmpegCommandBuilderTests
{
    [Fact]
    public void BuildCaptureArguments_UsesDesktopCaptureByDefault()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");

        var arguments = FfmpegCommandBuilder.BuildCaptureArguments(settings, "segment-%06d.mp4");

        Assert.Contains("desktop", arguments);
        Assert.DoesNotContain("-video_size", arguments);
        Assert.Contains("scale=trunc(iw/2)*2:trunc(ih/2)*2", arguments);
    }

    [Fact]
    public void BuildCaptureArguments_IncludesRegionArguments_WhenWindowBoundsProvided()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        var region = new CaptureRegion(100, 200, 1280, 720, "test window");

        var arguments = FfmpegCommandBuilder.BuildCaptureArguments(settings, "segment-%06d.mp4", region);

        Assert.Contains("-offset_x", arguments);
        Assert.Contains("100", arguments);
        Assert.Contains("-offset_y", arguments);
        Assert.Contains("200", arguments);
        Assert.Contains("-video_size", arguments);
        Assert.Contains("1280x720", arguments);
    }

    [Fact]
    public void BuildCaptureArguments_AppliesScaleDownFilter_When1080pModeSelected()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.OutputResolution = OutputResolutionMode.Max1080p;

        var arguments = FfmpegCommandBuilder.BuildCaptureArguments(settings, "segment-%06d.mp4");

        Assert.Contains(
            "scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2",
            arguments);
    }

    [Fact]
    public void BuildCaptureArguments_UsesBitrateWhenBitrateModeSelected()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.QualityInputMode = QualityInputMode.Bitrate;
        settings.QualityBitrateMbps = 14;
        settings.Normalize();

        var arguments = FfmpegCommandBuilder.BuildCaptureArguments(settings, "segment-%06d.mp4");

        Assert.Contains("-b:v", arguments);
        Assert.Contains("14M", arguments);
        Assert.DoesNotContain("-crf", arguments);
    }

    [Fact]
    public void BuildExportArguments_UsesCrfWhenCrfModeSelected()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.QualityInputMode = QualityInputMode.Crf;
        settings.QualityCrf = 23;
        settings.Normalize();

        var arguments = FfmpegCommandBuilder.BuildExportArguments(settings, "segments.txt", "clip.mp4");

        Assert.Contains("-crf", arguments);
        Assert.Contains(settings.QualityCrf.ToString(), arguments);
        Assert.DoesNotContain("-b:v", arguments);
    }

    [Fact]
    public void BuildExportArguments_UsesBitrateWhenBitrateModeSelected()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.QualityInputMode = QualityInputMode.Bitrate;
        settings.QualityBitrateMbps = 18;
        settings.Normalize();

        var arguments = FfmpegCommandBuilder.BuildExportArguments(settings, "segments.txt", "clip.mp4");

        Assert.Contains("-b:v", arguments);
        Assert.Contains("18M", arguments);
        Assert.DoesNotContain("-crf", arguments);
    }
}
