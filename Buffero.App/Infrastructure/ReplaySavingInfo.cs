namespace Buffero.App.Infrastructure;

public sealed record ReplaySavingInfo(
    string OutputPath,
    string? ActiveMatch,
    CaptureTargetWindow? TargetWindow);
