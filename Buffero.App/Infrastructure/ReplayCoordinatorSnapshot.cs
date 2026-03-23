using Buffero.Core.State;

namespace Buffero.App.Infrastructure;

public sealed record ReplayCoordinatorSnapshot(
    ReplayState State,
    string StatusMessage,
    string? ActiveMatch,
    bool IsCapturing,
    int BufferedSegmentCount,
    string? LastSavedClipPath,
    string FfmpegPath,
    string? SessionDirectory,
    string CaptureTargetDescription);
