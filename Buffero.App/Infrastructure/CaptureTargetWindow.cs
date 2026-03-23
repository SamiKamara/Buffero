using Buffero.Core.Capture;

namespace Buffero.App.Infrastructure;

public sealed record CaptureTargetWindow(
    IntPtr Handle,
    string ProcessName,
    string WindowLabel,
    int Left,
    int Top,
    int Width,
    int Height)
{
    public bool HasUsableBounds => Width >= 32 && Height >= 32;

    public string Description => $"{ProcessName} window {WindowLabel} ({Width}x{Height} at {Left},{Top})";

    public CaptureRegion ToCaptureRegion()
    {
        return new CaptureRegion(Left, Top, Width, Height, Description);
    }
}
