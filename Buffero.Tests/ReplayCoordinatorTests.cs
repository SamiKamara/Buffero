using Buffero.App.Infrastructure;
using Buffero.Core.Capture;
using Buffero.Core.Configuration;

namespace Buffero.Tests;

public sealed class ReplayCoordinatorTests
{
    [Fact]
    public void GetFinalizedSegments_ReturnsAllStableSegmentsOrdered()
    {
        var finalized = ReplayCoordinator.GetFinalizedSegments(
        [
            new SegmentInfo("3.mp4", 3, 100, DateTimeOffset.UtcNow.AddSeconds(-2)),
            new SegmentInfo("1.mp4", 1, 100, DateTimeOffset.UtcNow.AddSeconds(-6)),
            new SegmentInfo("2.mp4", 2, 100, DateTimeOffset.UtcNow.AddSeconds(-4))
        ]);

        Assert.Equal([1, 2, 3], finalized.Select(segment => segment.Sequence));
    }

    [Fact]
    public void GetFinalizedSegments_ReturnsSingleStableSegment_WhenOnlyOneExists()
    {
        var finalized = ReplayCoordinator.GetFinalizedSegments(
        [
            new SegmentInfo("1.mp4", 1, 100, DateTimeOffset.UtcNow.AddSeconds(-2))
        ]);

        Assert.Equal([1], finalized.Select(segment => segment.Sequence));
    }

    [Fact]
    public void ReadStableSegments_IncludesNewestClosedSegment()
    {
        using var tempDirectory = new TempDirectory();
        var firstPath = tempDirectory.CreateFile("segment-000001.mp4");
        var secondPath = tempDirectory.CreateFile("segment-000002.mp4");
        var stableTime = DateTime.UtcNow.AddSeconds(-5);
        File.SetLastWriteTimeUtc(firstPath, stableTime);
        File.SetLastWriteTimeUtc(secondPath, stableTime);

        var finalized = ReplayCoordinator.ReadStableSegments(
            tempDirectory.Path,
            DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.Equal([1, 2], finalized.Select(segment => segment.Sequence));
    }

    [Fact]
    public void ReadStableSegments_ExcludesFileThatIsStillOpen()
    {
        using var tempDirectory = new TempDirectory();
        var firstPath = tempDirectory.CreateFile("segment-000001.mp4");
        var secondPath = tempDirectory.CreateFile("segment-000002.mp4");
        var stableTime = DateTime.UtcNow.AddSeconds(-5);
        File.SetLastWriteTimeUtc(firstPath, stableTime);
        File.SetLastWriteTimeUtc(secondPath, stableTime);

        using var openHandle = new FileStream(secondPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var finalized = ReplayCoordinator.ReadStableSegments(
            tempDirectory.Path,
            DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.Equal([1], finalized.Select(segment => segment.Sequence));
    }

    [Fact]
    public void ShouldPreferFfmpegCapture_ReturnsTrue_ForWindowModeDisplayFallback()
    {
        var shouldPrefer = ReplayCoordinator.ShouldPreferFfmpegCapture(
            CaptureBackend.Native,
            CaptureMode.Display,
            CaptureMode.Window);

        Assert.True(shouldPrefer);
    }

    [Fact]
    public void ShouldPreferFfmpegCapture_ReturnsFalse_ForNativeWindowCapture()
    {
        var shouldPrefer = ReplayCoordinator.ShouldPreferFfmpegCapture(
            CaptureBackend.Native,
            CaptureMode.Window,
            CaptureMode.Window);

        Assert.False(shouldPrefer);
    }

    [Fact]
    public void ShouldPreferFfmpegCapture_ReturnsFalse_WhenFfmpegBackendWasAlreadyRequested()
    {
        var shouldPrefer = ReplayCoordinator.ShouldPreferFfmpegCapture(
            CaptureBackend.Ffmpeg,
            CaptureMode.Display,
            CaptureMode.Window);

        Assert.False(shouldPrefer);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"buffero-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateFile(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
