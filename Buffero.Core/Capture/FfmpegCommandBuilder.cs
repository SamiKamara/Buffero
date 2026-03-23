using System.Globalization;
using Buffero.Core.Configuration;

namespace Buffero.Core.Capture;

public static class FfmpegCommandBuilder
{
    public static IReadOnlyList<string> BuildCaptureArguments(AppSettings settings, string outputPattern, CaptureRegion? captureRegion = null)
    {
        var gop = Math.Max(settings.Fps * settings.SegmentSeconds, settings.Fps);
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
            "-preset", "veryfast",
            "-crf", settings.QualityCrf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p",
            "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
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

    public static IReadOnlyList<string> BuildExportArguments(AppSettings settings, string concatFilePath, string outputPath)
    {
        return
        [
            "-hide_banner",
            "-loglevel", "warning",
            "-y",
            "-f", "concat",
            "-safe", "0",
            "-i", concatFilePath,
            "-an",
            "-c:v", "libx264",
            "-preset", "fast",
            "-crf", settings.QualityCrf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            outputPath
        ];
    }
}
