namespace Buffero.App.Infrastructure;

public sealed record ReplaySavedInfo(
    string ClipPath,
    string? ActiveMatch,
    CaptureTargetWindow? TargetWindow);
