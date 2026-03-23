namespace Buffero.Core.Capture;

public sealed record CaptureRegion(
    int X,
    int Y,
    int Width,
    int Height,
    string Description);
