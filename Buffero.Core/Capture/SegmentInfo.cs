namespace Buffero.Core.Capture;

public sealed record SegmentInfo(
    string Path,
    int Sequence,
    long SizeBytes,
    DateTimeOffset LastWriteUtc);
