using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Buffero.Core.Capture;
using Buffero.Core.Configuration;

namespace Buffero.App.Infrastructure;

internal static class FfmpegCaptureSourceResolver
{
    private static readonly ConcurrentDictionary<string, string> FilterCache = new(StringComparer.OrdinalIgnoreCase);

    public static FfmpegCaptureSource? TryResolve(
        string ffmpegPath,
        AppSettings settings,
        CaptureMode requestedCaptureMode,
        CaptureMode effectiveCaptureMode,
        CaptureTargetWindow? targetWindow)
    {
        if (!SupportsFilter(ffmpegPath, "gfxcapture"))
        {
            return null;
        }

        string? input;
        string sourceName;

        if (requestedCaptureMode == CaptureMode.Window && targetWindow is not null && targetWindow.Handle != IntPtr.Zero)
        {
            input = CreateWindowInput(targetWindow, settings);
            sourceName = "gfxcapture (window)";
        }
        else if (effectiveCaptureMode == CaptureMode.Window)
        {
            input = CreateWindowInput(targetWindow, settings);
            sourceName = "gfxcapture (window)";
        }
        else
        {
            input = CreateDisplayInput(targetWindow, settings);
            sourceName = "gfxcapture (display)";
        }

        return input is null
            ? null
            : new FfmpegCaptureSource(
                sourceName,
                ["-f", "lavfi", "-i", input],
                "hwdownload,format=bgra");
    }

    private static string? CreateWindowInput(CaptureTargetWindow? targetWindow, AppSettings settings)
    {
        if (targetWindow is null || targetWindow.Handle == IntPtr.Zero)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"gfxcapture=hwnd={FormatHandle(targetWindow.Handle)}:max_framerate={settings.Fps}:capture_cursor=1:capture_border=0:display_border=0");
    }

    private static string? CreateDisplayInput(CaptureTargetWindow? targetWindow, AppSettings settings)
    {
        var monitor = MonitorLocator.GetCaptureMonitor(targetWindow);
        if (monitor == IntPtr.Zero)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"gfxcapture=hmonitor={FormatHandle(monitor)}:max_framerate={settings.Fps}:capture_cursor=1:display_border=0");
    }

    private static bool SupportsFilter(string ffmpegPath, string filterName)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            return false;
        }

        try
        {
            var filters = FilterCache.GetOrAdd(ffmpegPath, ProbeFilters);
            return filters.IndexOf($" {filterName} ", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ProbeFilters(string ffmpegPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-filters");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return string.Empty;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return $"{stdout}{Environment.NewLine}{stderr}";
    }

    private static string FormatHandle(IntPtr handle)
    {
        return unchecked((ulong)handle.ToInt64()).ToString(CultureInfo.InvariantCulture);
    }
}
