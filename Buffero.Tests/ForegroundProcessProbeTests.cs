using Buffero.App.Infrastructure;

namespace Buffero.Tests;

public sealed class ForegroundProcessProbeTests
{
    [Fact]
    public void SelectBestTargetWindow_PrefersLargestWindow()
    {
        var selected = ForegroundProcessProbe.SelectBestTargetWindow(
        [
            new CaptureTargetWindow((IntPtr)1, "spaceengineers", "(untitled)", 100, 100, 716, 403),
            new CaptureTargetWindow((IntPtr)2, "spaceengineers", "\"Space Engineers\"", 0, 0, 1920, 1080)
        ],
        foregroundWindow: IntPtr.Zero);

        Assert.NotNull(selected);
        Assert.Equal((IntPtr)2, selected!.Handle);
    }

    [Fact]
    public void SelectBestTargetWindow_PrefersTitledWindow_WhenSizesMatch()
    {
        var selected = ForegroundProcessProbe.SelectBestTargetWindow(
        [
            new CaptureTargetWindow((IntPtr)1, "spaceengineers", "(untitled)", 0, 0, 1280, 720),
            new CaptureTargetWindow((IntPtr)2, "spaceengineers", "\"Space Engineers\"", 100, 100, 1280, 720)
        ],
        foregroundWindow: IntPtr.Zero);

        Assert.NotNull(selected);
        Assert.Equal((IntPtr)2, selected!.Handle);
    }

    [Fact]
    public void SelectBestTargetWindow_PrefersForegroundWindow_WhenCandidatesOtherwiseTie()
    {
        var selected = ForegroundProcessProbe.SelectBestTargetWindow(
        [
            new CaptureTargetWindow((IntPtr)1, "spaceengineers", "\"Window A\"", 0, 0, 1280, 720),
            new CaptureTargetWindow((IntPtr)2, "spaceengineers", "\"Window B\"", 100, 100, 1280, 720)
        ],
        foregroundWindow: (IntPtr)2);

        Assert.NotNull(selected);
        Assert.Equal((IntPtr)2, selected!.Handle);
    }
}
