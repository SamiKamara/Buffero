using Buffero.Core.Configuration;

namespace Buffero.App.Infrastructure;

internal static class OutputResolutionCalculator
{
    public static (uint Width, uint Height) GetOutputSize(int sourceWidth, int sourceHeight, OutputResolutionMode mode)
    {
        if (sourceWidth < 2 || sourceHeight < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Capture targets must have usable dimensions.");
        }

        var maxWidth = sourceWidth;
        var maxHeight = sourceHeight;

        switch (mode)
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
        return ((uint)width, (uint)height);
    }

    public static uint EstimateBitrate(int width, int height, int fps, int qualityCrf)
    {
        var normalizedQuality = 1d - ((Math.Clamp(qualityCrf, 18, 35) - 18d) / 17d);
        var bitsPerPixel = 0.05d + (normalizedQuality * 0.12d);
        var bitrate = width * height * Math.Max(15, fps) * bitsPerPixel;
        return (uint)Math.Clamp((long)Math.Round(bitrate), 2_000_000L, 40_000_000L);
    }

    private static int MakeEven(int value)
    {
        return value % 2 == 0 ? value : value - 1;
    }
}
