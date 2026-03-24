using Buffero.Core.Capture;
using Buffero.Core.Configuration;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Buffero.App.Infrastructure;

internal static class NativeReplayExporter
{
    public static async Task ExportAsync(
        IReadOnlyList<SegmentInfo> segments,
        AppSettings settings,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (segments.Count == 0)
        {
            throw new InvalidOperationException("No replay segments were available for native export.");
        }

        var composition = new MediaComposition();
        foreach (var segment in segments)
        {
            var file = await StorageFile.GetFileFromPathAsync(segment.Path);
            var clip = await MediaClip.CreateFromFileAsync(file);
            composition.Clips.Add(clip);
        }

        var firstFile = await StorageFile.GetFileFromPathAsync(segments[0].Path);
        var firstVideoProperties = await firstFile.Properties.GetVideoPropertiesAsync();
        var (outputWidth, outputHeight) = OutputResolutionCalculator.GetOutputSize(
            (int)firstVideoProperties.Width,
            (int)firstVideoProperties.Height,
            settings.OutputResolution);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outputPath)!);
        var outputFile = await folder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.ReplaceExisting);

        var profile = new MediaEncodingProfile();
        profile.Container.Subtype = "MPEG4";
        profile.Video.Subtype = "H264";
        profile.Video.Width = outputWidth;
        profile.Video.Height = outputHeight;
        profile.Video.Bitrate = OutputResolutionCalculator.EstimateBitrate((int)outputWidth, (int)outputHeight, settings.Fps, settings.QualityCrf);
        profile.Video.FrameRate.Numerator = (uint)settings.Fps;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;

        var result = await composition.RenderToFileAsync(outputFile, MediaTrimmingPreference.Precise, profile).AsTask(cancellationToken);
        if (result != TranscodeFailureReason.None)
        {
            throw new InvalidOperationException($"Native export failed with {result}.");
        }
    }
}
