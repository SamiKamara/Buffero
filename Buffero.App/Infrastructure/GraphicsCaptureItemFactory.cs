using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace Buffero.App.Infrastructure;

internal static class GraphicsCaptureItemFactory
{
    private const string GraphicsCaptureItemRuntimeClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IGraphicsCaptureItemInteropGuid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor(IntPtr monitor, [In] ref Guid iid);
    }

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr stringHandle);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr stringHandle);

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(hwnd, IntPtr.Zero);
        var factory = GetActivationFactory();
        var interop = (IGraphicsCaptureItemInterop)factory;
        var itemPointer = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
        return FromPointer(itemPointer);
    }

    public static GraphicsCaptureItem CreateForMonitor(IntPtr hmon)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(hmon, IntPtr.Zero);
        var factory = GetActivationFactory();
        var interop = (IGraphicsCaptureItemInterop)factory;
        var itemPointer = interop.CreateForMonitor(hmon, GraphicsCaptureItemGuid);
        return FromPointer(itemPointer);
    }

    private static object GetActivationFactory()
    {
        WindowsCreateString(
            GraphicsCaptureItemRuntimeClassName,
            GraphicsCaptureItemRuntimeClassName.Length,
            out var runtimeClassName).ThrowIfFailed();

        try
        {
            var interopGuid = IGraphicsCaptureItemInteropGuid;
            RoGetActivationFactory(runtimeClassName, ref interopGuid, out var factoryPointer).ThrowIfFailed();
            try
            {
                return Marshal.GetObjectForIUnknown(factoryPointer);
            }
            finally
            {
                Marshal.Release(factoryPointer);
            }
        }
        finally
        {
            WindowsDeleteString(runtimeClassName);
        }
    }

    private static GraphicsCaptureItem FromPointer(IntPtr itemPointer)
    {
        if (itemPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("Windows.Graphics.Capture could not create a capture item for the selected target.");
        }

        try
        {
            return (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPointer);
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

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct POINT(int X, int Y);
}
