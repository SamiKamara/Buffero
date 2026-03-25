namespace Buffero.Core.Capture;

public sealed record FfmpegCaptureSource(
    string Name,
    IReadOnlyList<string> InputArguments,
    string? FilterPrefix = null);
