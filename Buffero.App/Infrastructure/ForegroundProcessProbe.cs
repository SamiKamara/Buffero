using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Buffero.Core.Capture;
using Buffero.Core.Configuration;

namespace Buffero.App.Infrastructure;

public static class ForegroundProcessProbe
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static string? GetForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<string> GetRunningProcessNames()
    {
        return Process.GetProcesses()
            .Select(process =>
            {
                try
                {
                    return process.ProcessName;
                }
                finally
                {
                    process.Dispose();
                }
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static CaptureRegion? TryResolveCaptureRegion(string? processName)
    {
        return TryResolveTargetWindow(processName)?.ToCaptureRegion();
    }

    public static CaptureTargetWindow? TryResolveTargetWindow(string? processName)
    {
        var normalized = HotkeyBinding.NormalizeExecutableToken(processName);
        var foregroundWindow = GetForegroundWindow();
        var candidates = new List<CaptureTargetWindow>();

        if (TryGetTargetWindow(foregroundWindow, normalized, requireMatchingProcess: !string.IsNullOrWhiteSpace(normalized), out var foregroundTarget)
            && foregroundTarget is not null)
        {
            candidates.Add(foregroundTarget);
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return SelectBestTargetWindow(candidates, foregroundWindow);
        }

        var seenHandles = new HashSet<IntPtr>(candidates.Select(candidate => candidate.Handle));

        EnumWindows((hWnd, _) =>
        {
            if (!seenHandles.Add(hWnd))
            {
                return true;
            }

            if (TryGetTargetWindow(hWnd, normalized, requireMatchingProcess: true, out var targetWindow)
                && targetWindow is not null)
            {
                candidates.Add(targetWindow);
            }

            return true;
        }, IntPtr.Zero);

        return SelectBestTargetWindow(candidates, foregroundWindow);
    }

    private static bool TryGetTargetWindow(IntPtr hWnd, string? expectedProcessName, bool requireMatchingProcess, out CaptureTargetWindow? targetWindow)
    {
        targetWindow = null;

        if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd) || IsIconic(hWnd))
        {
            return false;
        }

        GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0)
        {
            return false;
        }

        string processName;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = HotkeyBinding.NormalizeExecutableToken(process.ProcessName);
        }
        catch
        {
            return false;
        }

        if (requireMatchingProcess
            && !string.Equals(processName, expectedProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!GetWindowRect(hWnd, out var rect))
        {
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 32 || height < 32)
        {
            return false;
        }

        targetWindow = new CaptureTargetWindow(
            hWnd,
            processName,
            GetWindowLabel(hWnd),
            rect.Left,
            rect.Top,
            width,
            height);
        return true;
    }

    internal static CaptureTargetWindow? SelectBestTargetWindow(IEnumerable<CaptureTargetWindow> windows, IntPtr foregroundWindow)
    {
        return windows
            .OrderByDescending(window => (long)window.Width * window.Height)
            .ThenByDescending(window => !string.Equals(window.WindowLabel, "(untitled)", StringComparison.Ordinal))
            .ThenByDescending(window => window.Handle == foregroundWindow)
            .FirstOrDefault();
    }

    private static string GetWindowLabel(IntPtr hWnd)
    {
        var builder = new StringBuilder(512);
        _ = GetWindowText(hWnd, builder, builder.Capacity);
        var title = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(title) ? "(untitled)" : $"\"{title}\"";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
