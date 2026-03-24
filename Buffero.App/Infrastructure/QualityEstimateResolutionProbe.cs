using Buffero.Core.Capture;
using Buffero.Core.Configuration;

namespace Buffero.App.Infrastructure;

internal enum QualityEstimateSource
{
    ConfiguredGameWindow,
    PrimaryScreen,
    Reference
}

internal readonly record struct QualityEstimateResolution(
    int Width,
    int Height,
    QualityEstimateSource Source,
    string? ProcessName = null);

internal static class QualityEstimateResolutionProbe
{
    public static QualityEstimateResolution Resolve(
        OutputResolutionMode outputResolution,
        IEnumerable<string> configuredExecutables,
        string? preferredProcessName = null)
    {
        var configuredGameWindow = TryResolveConfiguredGameWindow(configuredExecutables, preferredProcessName);
        if (configuredGameWindow is { HasUsableBounds: true } targetWindow)
        {
            var (windowWidth, windowHeight) = CaptureQualityEstimator.GetEstimatedOutputSize(
                outputResolution,
                targetWindow.Width,
                targetWindow.Height);
            return new QualityEstimateResolution(
                windowWidth,
                windowHeight,
                QualityEstimateSource.ConfiguredGameWindow,
                targetWindow.ProcessName);
        }

        if (DisplayResolutionProbe.TryGetPrimaryScreenResolution(out var primaryScreenWidth, out var primaryScreenHeight))
        {
            var (screenWidth, screenHeight) = CaptureQualityEstimator.GetEstimatedOutputSize(
                outputResolution,
                primaryScreenWidth,
                primaryScreenHeight);
            return new QualityEstimateResolution(
                screenWidth,
                screenHeight,
                QualityEstimateSource.PrimaryScreen);
        }

        var (referenceWidth, referenceHeight) = CaptureQualityEstimator.GetReferenceOutputSize(outputResolution);
        return new QualityEstimateResolution(referenceWidth, referenceHeight, QualityEstimateSource.Reference);
    }

    public static QualityEstimateResolution Resolve(AppSettings settings, string? preferredProcessName = null)
    {
        return Resolve(settings.OutputResolution, settings.AllowedExecutables, preferredProcessName);
    }

    private static CaptureTargetWindow? TryResolveConfiguredGameWindow(
        IEnumerable<string> configuredExecutables,
        string? preferredProcessName = null)
    {
        var normalizedExecutables = configuredExecutables
            .Select(HotkeyBinding.NormalizeExecutableToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedExecutables.Count == 0)
        {
            return null;
        }

        var preferredToken = HotkeyBinding.NormalizeExecutableToken(preferredProcessName);
        if (!string.IsNullOrWhiteSpace(preferredToken)
            && normalizedExecutables.Contains(preferredToken, StringComparer.OrdinalIgnoreCase))
        {
            var preferredWindow = ForegroundProcessProbe.TryResolveTargetWindow(preferredToken);
            if (preferredWindow is not null)
            {
                return preferredWindow;
            }
        }

        foreach (var executable in normalizedExecutables)
        {
            if (string.Equals(executable, preferredToken, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var window = ForegroundProcessProbe.TryResolveTargetWindow(executable);
            if (window is not null)
            {
                return window;
            }
        }

        return null;
    }
}
