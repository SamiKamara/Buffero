using System.Globalization;
using Buffero.Core.Configuration;

namespace Buffero.Core.Capture;

public static class FfmpegCommandBuilder
{
    public static IReadOnlyList<string> BuildCaptureArguments(AppSettings settings, string outputPattern, CaptureRegion? captureRegion = null)
    {
        var gop = Math.Max(settings.Fps * settings.SegmentSeconds, settings.Fps);
        var videoFilter = BuildCaptureVideoFilter(settings.OutputResolution);
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "warning",
            "-y",
            "-f", "gdigrab",
            "-framerate", settings.Fps.ToString(CultureInfo.InvariantCulture),
            "-draw_mouse", "1"
        };

        if (captureRegion is not null)
        {
            arguments.AddRange(
            [
                "-offset_x", captureRegion.X.ToString(CultureInfo.InvariantCulture),
                "-offset_y", captureRegion.Y.ToString(CultureInfo.InvariantCulture),
                "-video_size", $"{captureRegion.Width}x{captureRegion.Height}"
            ]);
        }

        arguments.AddRange(
        [
            "-i", "desktop",
            "-an",
            "-c:v", "libx264",
            "-preset", "veryfast"
        ]);

        arguments.AddRange(BuildQualityArguments(settings));

        arguments.AddRange(
        [
            "-pix_fmt", "yuv420p",
            "-vf", videoFilter,
            "-g", gop.ToString(CultureInfo.InvariantCulture),
            "-keyint_min", gop.ToString(CultureInfo.InvariantCulture),
            "-sc_threshold", "0",
            "-f", "segment",
            "-segment_time", settings.SegmentSeconds.ToString(CultureInfo.InvariantCulture),
            "-segment_format", "mp4",
            "-reset_timestamps", "1",
            "-strftime", "0",
            outputPattern
        ]);

        return arguments;
    }

    private static string BuildCaptureVideoFilter(OutputResolutionMode outputResolution)
    {
        const string ensureEvenDimensionsFilter = "scale=trunc(iw/2)*2:trunc(ih/2)*2";

        return outputResolution switch
        {
            OutputResolutionMode.Max1080p =>
                "scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease,"
                + ensureEvenDimensionsFilter,
            OutputResolutionMode.Max720p =>
                "scale=w='min(1280,iw)':h='min(720,ih)':force_original_aspect_ratio=decrease,"
                + ensureEvenDimensionsFilter,
            _ => ensureEvenDimensionsFilter
        };
    }

    public static IReadOnlyList<string> BuildExportArguments(AppSettings settings, string concatFilePath, string outputPath)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "warning",
            "-y",
            "-f", "concat",
            "-safe", "0",
            "-i", concatFilePath,
            "-an",
            "-c:v", "libx264",
            "-preset", "fast"
        };

        arguments.AddRange(BuildQualityArguments(settings));
        arguments.AddRange(
        [
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            outputPath
        ]);

        return arguments;
    }

    private static IReadOnlyList<string> BuildQualityArguments(AppSettings settings)
    {
        return settings.QualityInputMode == QualityInputMode.Bitrate
            ? ["-b:v", CaptureQualityEstimator.FormatFfmpegBitrate(settings.QualityBitrateMbps)]
            : ["-crf", settings.QualityCrf.ToString(CultureInfo.InvariantCulture)];
    }
}
