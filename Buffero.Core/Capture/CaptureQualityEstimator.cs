using Buffero.Core.Configuration;

namespace Buffero.Core.Capture;

public static class CaptureQualityEstimator
{
    public const int MinCrf = 18;
    public const int MaxCrf = 35;
    public const int MinBitrateMbps = 2;
    public const int MaxBitrateMbps = 120;

    private const int MinFps = 15;
    private const int MaxFps = 60;
    private const long BitsPerMegabit = 1_000_000L;
    private const double MinimumBitsPerPixel = 0.05d;
    private const double BitsPerPixelRange = 0.12d;

    public static int ClampCrf(int qualityCrf) => Math.Clamp(qualityCrf, MinCrf, MaxCrf);

    public static int ClampBitrateMbps(int bitrateMbps) => Math.Clamp(bitrateMbps, MinBitrateMbps, MaxBitrateMbps);

    public static int ClampFps(int fps) => Math.Clamp(fps, MinFps, MaxFps);

    public static (int Width, int Height) GetReferenceOutputSize(OutputResolutionMode outputResolution)
    {
        return outputResolution switch
        {
            OutputResolutionMode.Max720p => (1280, 720),
            _ => (1920, 1080)
        };
    }

    public static (int Width, int Height) ResolveOutputSize(int sourceWidth, int sourceHeight, OutputResolutionMode outputResolution)
    {
        if (sourceWidth < 2 || sourceHeight < 2)
        {
            return GetReferenceOutputSize(outputResolution);
        }

        var maxWidth = sourceWidth;
        var maxHeight = sourceHeight;

        switch (outputResolution)
        {
            case OutputResolutionMode.Max1080p:
                maxWidth = Math.Min(maxWidth, 1920);
                maxHeight = Math.Min(maxHeight, 1080);
                break;
            case OutputResolutionMode.Max720p:
                maxWidth = Math.Min(maxWidth, 1280);
                maxHeight = Math.Min(maxHeight, 720);
                break;
        }

        var scale = Math.Min(maxWidth / (double)sourceWidth, maxHeight / (double)sourceHeight);
        if (scale > 1d)
        {
            scale = 1d;
        }

        var width = MakeEven(Math.Max(2, (int)Math.Round(sourceWidth * scale)));
        var height = MakeEven(Math.Max(2, (int)Math.Round(sourceHeight * scale)));
        return (width, height);
    }

    public static (int Width, int Height) GetEstimatedOutputSize(
        OutputResolutionMode outputResolution,
        int? sourceWidth = null,
        int? sourceHeight = null)
    {
        return sourceWidth is > 1 && sourceHeight is > 1
            ? ResolveOutputSize(sourceWidth.Value, sourceHeight.Value, outputResolution)
            : GetReferenceOutputSize(outputResolution);
    }

    public static string GetReferenceOutputLabel(OutputResolutionMode outputResolution)
    {
        return outputResolution switch
        {
            OutputResolutionMode.Max720p => "720p",
            _ => "1080p"
        };
    }

    public static long EstimateBitrateBitsPerSecond(int width, int height, int fps, int qualityCrf)
    {
        var effectiveWidth = Math.Max(2, width);
        var effectiveHeight = Math.Max(2, height);
        var effectiveFps = ClampFps(fps);
        var normalizedQuality = 1d - ((ClampCrf(qualityCrf) - MinCrf) / (double)(MaxCrf - MinCrf));
        var bitsPerPixel = MinimumBitsPerPixel + (normalizedQuality * BitsPerPixelRange);
        var bitrate = effectiveWidth * effectiveHeight * effectiveFps * bitsPerPixel;
        return Math.Clamp(
            (long)Math.Round(bitrate),
            MegabitsPerSecondToBitsPerSecond(MinBitrateMbps),
            MegabitsPerSecondToBitsPerSecond(MaxBitrateMbps));
    }

    public static int EstimateBitrateMbps(OutputResolutionMode outputResolution, int fps, int qualityCrf)
    {
        var (width, height) = GetReferenceOutputSize(outputResolution);
        return BitsPerSecondToMegabitsPerSecond(EstimateBitrateBitsPerSecond(width, height, fps, qualityCrf));
    }

    public static int EstimateBitrateMbps(int width, int height, int fps, int qualityCrf)
    {
        return BitsPerSecondToMegabitsPerSecond(EstimateBitrateBitsPerSecond(width, height, fps, qualityCrf));
    }

    public static int EstimateCrf(int width, int height, int fps, long bitrateBitsPerSecond)
    {
        var effectiveWidth = Math.Max(2, width);
        var effectiveHeight = Math.Max(2, height);
        var effectiveFps = ClampFps(fps);
        var clampedBitrate = Math.Clamp(
            bitrateBitsPerSecond,
            MegabitsPerSecondToBitsPerSecond(MinBitrateMbps),
            MegabitsPerSecondToBitsPerSecond(MaxBitrateMbps));
        var bitsPerPixel = clampedBitrate / (double)(effectiveWidth * effectiveHeight * effectiveFps);
        var normalizedQuality = Math.Clamp((bitsPerPixel - MinimumBitsPerPixel) / BitsPerPixelRange, 0d, 1d);
        var crf = MinCrf + ((1d - normalizedQuality) * (MaxCrf - MinCrf));
        return ClampCrf((int)Math.Round(crf));
    }

    public static int EstimateCrf(OutputResolutionMode outputResolution, int fps, int bitrateMbps)
    {
        var (width, height) = GetReferenceOutputSize(outputResolution);
        return EstimateCrf(width, height, fps, MegabitsPerSecondToBitsPerSecond(bitrateMbps));
    }

    public static long ResolveTargetBitrateBitsPerSecond(
        QualityInputMode qualityInputMode,
        int bitrateMbps,
        int qualityCrf,
        int width,
        int height,
        int fps)
    {
        return qualityInputMode == QualityInputMode.Bitrate
            ? MegabitsPerSecondToBitsPerSecond(bitrateMbps)
            : EstimateBitrateBitsPerSecond(width, height, fps, qualityCrf);
    }

    public static string FormatFfmpegBitrate(int bitrateMbps) => $"{ClampBitrateMbps(bitrateMbps)}M";

    public static long MegabitsPerSecondToBitsPerSecond(int bitrateMbps) => ClampBitrateMbps(bitrateMbps) * BitsPerMegabit;

    public static int BitsPerSecondToMegabitsPerSecond(long bitrateBitsPerSecond)
    {
        return ClampBitrateMbps((int)Math.Round(bitrateBitsPerSecond / (double)BitsPerMegabit));
    }

    private static int MakeEven(int value) => value % 2 == 0 ? value : value - 1;
}
