using Buffero.Core.Configuration;
using Buffero.Core.State;

namespace Buffero.App.Infrastructure;

public sealed record ReplayCoordinatorSnapshot(
    bool IsReplayBufferEnabled,
    BufferActivationMode BufferActivationMode,
    ReplayState State,
    string StatusMessage,
    string? ActiveMatch,
    string? EligibleMatch,
    bool IsCapturing,
    int BufferedSegmentCount,
    string? LastSavedClipPath,
    CaptureBackend ConfiguredCaptureBackend,
    CaptureBackend ActiveCaptureBackend,
    string FfmpegPath,
    string? SessionDirectory,
    string CaptureTargetDescription,
    CaptureTargetWindow? TargetWindow,
    CaptureTargetWindow? EligibleTargetWindow);
