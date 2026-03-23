using Buffero.Core.Capture;

namespace Buffero.Tests;

public sealed class ExportDiskSpaceGuardTests
{
    [Fact]
    public void Evaluate_UsesCriticalReserveForSmallExports()
    {
        SegmentInfo[] snapshot =
        [
            new SegmentInfo("segment-000001.mp4", 1, 64L * 1024L * 1024L, DateTimeOffset.UtcNow)
        ];

        var check = ExportDiskSpaceGuard.Evaluate(snapshot, 600L * 1024L * 1024L);

        Assert.True(check.CanExport);
        Assert.Equal(ExportDiskSpaceGuard.CriticalFreeSpaceReserveBytes, check.RequiredFreeBytes);
    }

    [Fact]
    public void Evaluate_RequiresSnapshotSizePlusSlack_ForLargeExports()
    {
        SegmentInfo[] snapshot =
        [
            new SegmentInfo("segment-000001.mp4", 1, 700L * 1024L * 1024L, DateTimeOffset.UtcNow),
            new SegmentInfo("segment-000002.mp4", 2, 300L * 1024L * 1024L, DateTimeOffset.UtcNow)
        ];

        var check = ExportDiskSpaceGuard.Evaluate(snapshot, 1024L * 1024L * 1024L);

        Assert.False(check.CanExport);
        Assert.Equal(1128L * 1024L * 1024L, check.RequiredFreeBytes);
    }

    [Fact]
    public void Evaluate_TreatsNegativeSegmentSizesAsZero()
    {
        SegmentInfo[] snapshot =
        [
            new SegmentInfo("segment-000001.mp4", 1, -100, DateTimeOffset.UtcNow)
        ];

        var check = ExportDiskSpaceGuard.Evaluate(snapshot, ExportDiskSpaceGuard.CriticalFreeSpaceReserveBytes);

        Assert.True(check.CanExport);
        Assert.Equal(ExportDiskSpaceGuard.CriticalFreeSpaceReserveBytes, check.RequiredFreeBytes);
    }
}
