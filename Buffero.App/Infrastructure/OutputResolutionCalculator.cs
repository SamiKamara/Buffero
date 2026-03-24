using Buffero.Core.Configuration;
using Buffero.Core.Capture;

namespace Buffero.App.Infrastructure;

internal static class OutputResolutionCalculator
{
    public static (uint Width, uint Height) GetOutputSize(int sourceWidth, int sourceHeight, OutputResolutionMode mode)
    {
        if (sourceWidth < 2 || sourceHeight < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Capture targets must have usable dimensions.");
        }

        var (width, height) = CaptureQualityEstimator.ResolveOutputSize(sourceWidth, sourceHeight, mode);
        return ((uint)width, (uint)height);
    }

    public static uint EstimateBitrate(int width, int height, int fps, int qualityCrf)
    {
        return (uint)CaptureQualityEstimator.EstimateBitrateBitsPerSecond(width, height, fps, qualityCrf);
    }
}
