namespace Buffero.Core.Capture;

public sealed record ExportDiskSpaceCheck(
    bool CanExport,
    long RequiredFreeBytes,
    long AvailableFreeBytes);

public static class ExportDiskSpaceGuard
{
    public const long CriticalFreeSpaceReserveBytes = 512L * 1024L * 1024L;
    public const long ExportSlackBytes = 128L * 1024L * 1024L;

    public static ExportDiskSpaceCheck Evaluate(IEnumerable<SegmentInfo> segments, long availableFreeBytes)
    {
        var requiredFreeBytes = EstimateRequiredFreeBytes(segments);
        return new ExportDiskSpaceCheck(
            availableFreeBytes >= requiredFreeBytes,
            requiredFreeBytes,
            availableFreeBytes);
    }

    public static long EstimateRequiredFreeBytes(IEnumerable<SegmentInfo> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var snapshotBytes = segments.Sum(segment => Math.Max(0L, segment.SizeBytes));
        return Math.Max(CriticalFreeSpaceReserveBytes, snapshotBytes + ExportSlackBytes);
    }
}
