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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

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

        bounds = new MonitorBounds(
            monitorInfo.rcMonitor.Left,
            monitorInfo.rcMonitor.Top,
            monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left,
            monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top);
        return bounds.Width > 0 && bounds.Height > 0;
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
