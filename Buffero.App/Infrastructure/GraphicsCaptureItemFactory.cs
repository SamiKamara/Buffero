using System.Runtime.InteropServices;
using Buffero.Core.Capture;
using Windows.Graphics.Capture;
using WinRT;

namespace Buffero.App.Infrastructure;

internal static class GraphicsCaptureItemFactory
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor(IntPtr monitor, [In] ref Guid iid);
    }

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(hwnd, IntPtr.Zero);
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemPointer = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
        return FromPointer(itemPointer);
    }

    public static GraphicsCaptureItem CreateForMonitor(IntPtr hmon)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(hmon, IntPtr.Zero);
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemPointer = interop.CreateForMonitor(hmon, GraphicsCaptureItemGuid);
        return FromPointer(itemPointer);
    }

    private static GraphicsCaptureItem FromPointer(IntPtr itemPointer)
    {
        if (itemPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("Windows.Graphics.Capture could not create a capture item for the selected target.");
        }

        try
        {
            return GraphicsCaptureItem.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }
}

internal static class MonitorLocator
{
    private const uint MonitorDefaultToPrimary = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    public static IntPtr GetCaptureMonitor(CaptureTargetWindow? targetWindow)
    {
        if (targetWindow is not null && targetWindow.Handle != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(targetWindow.Handle, MonitorDefaultToPrimary);
            if (monitor != IntPtr.Zero)
            {
                return monitor;
            }
        }

        return MonitorFromPoint(new POINT(0, 0), MonitorDefaultToPrimary);
    }

    public static bool TryGetMonitorBounds(CaptureTargetWindow? targetWindow, out MonitorBounds bounds)
    {
        return TryGetMonitorArea(targetWindow, useWorkArea: false, out bounds);
    }

    public static bool TryGetMonitorWorkArea(CaptureTargetWindow? targetWindow, out MonitorBounds bounds)
    {
        return TryGetMonitorArea(targetWindow, useWorkArea: true, out bounds);
    }

    public static bool TryGetMonitorIndex(CaptureTargetWindow? targetWindow, out int monitorIndex)
    {
        var targetMonitor = GetCaptureMonitor(targetWindow);
        if (targetMonitor == IntPtr.Zero)
        {
            monitorIndex = -1;
            return false;
        }

        var currentIndex = 0;
        var found = false;
        var resolvedIndex = -1;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            if (hMonitor == targetMonitor)
            {
                resolvedIndex = currentIndex;
                found = true;
                return false;
            }

            currentIndex++;
            return true;
        }, IntPtr.Zero);

        monitorIndex = resolvedIndex;
        return found;
    }

    public static bool ShouldPreferMonitorCapture(CaptureTargetWindow? targetWindow)
    {
        if (targetWindow is null || !TryGetMonitorBounds(targetWindow, out var monitorBounds))
        {
            return false;
        }

        const int tolerance = 2;

        return Math.Abs(targetWindow.Left - monitorBounds.Left) <= tolerance
            && Math.Abs(targetWindow.Top - monitorBounds.Top) <= tolerance
            && Math.Abs(targetWindow.Width - monitorBounds.Width) <= tolerance
            && Math.Abs(targetWindow.Height - monitorBounds.Height) <= tolerance;
    }

    private static bool TryGetMonitorArea(CaptureTargetWindow? targetWindow, bool useWorkArea, out MonitorBounds bounds)
    {
        var monitor = GetCaptureMonitor(targetWindow);
        if (monitor == IntPtr.Zero)
        {
            bounds = default;
            return false;
        }

        var monitorInfo = new MONITORINFO
        {
            cbSize = Marshal.SizeOf<MONITORINFO>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            bounds = default;
            return false;
        }

        var area = useWorkArea ? monitorInfo.rcWork : monitorInfo.rcMonitor;
        bounds = new MonitorBounds(
            area.Left,
            area.Top,
            area.Right - area.Left,
            area.Bottom - area.Top);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct POINT(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
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

internal readonly record struct MonitorBounds(int Left, int Top, int Width, int Height)
{
    public CaptureRegion ToCaptureRegion()
    {
        return new CaptureRegion(Left, Top, Width, Height, $"Display region ({Width}x{Height} at {Left},{Top})");
    }
}
