using Buffero.Core.Detection;

namespace Buffero.Tests;

public sealed class GameEligibilityDebouncerTests
{
    [Fact]
    public void Observe_WaitsForDebounceBeforePromotingNewMatch()
    {
        var debouncer = new GameEligibilityDebouncer(TimeSpan.FromSeconds(2));
        var start = new DateTimeOffset(2026, 3, 23, 12, 0, 0, TimeSpan.Zero);

        var first = debouncer.Observe("cs2.exe", start);
        var second = debouncer.Observe("cs2", start.AddSeconds(1));
        var third = debouncer.Observe("cs2", start.AddSeconds(2));

        Assert.Null(first.StableExecutable);
        Assert.False(first.Changed);
        Assert.Null(second.StableExecutable);
        Assert.False(second.Changed);
        Assert.Equal("cs2", third.StableExecutable);
        Assert.True(third.Changed);
    }

    [Fact]
    public void Observe_WaitsForDebounceBeforeClearingStableMatch()
    {
        var debouncer = new GameEligibilityDebouncer(TimeSpan.FromSeconds(2));
        var start = new DateTimeOffset(2026, 3, 23, 12, 0, 0, TimeSpan.Zero);

        _ = debouncer.Observe("valorant", start);
        _ = debouncer.Observe("valorant", start.AddSeconds(2));

        var firstLoss = debouncer.Observe(null, start.AddSeconds(2.5));
        var cleared = debouncer.Observe(null, start.AddSeconds(4.5));

        Assert.Equal("valorant", firstLoss.StableExecutable);
        Assert.False(firstLoss.Changed);
        Assert.Null(cleared.StableExecutable);
        Assert.True(cleared.Changed);
    }

    [Fact]
    public void Observe_SwitchesStableMatchOnlyAfterNewCandidateSticks()
    {
        var debouncer = new GameEligibilityDebouncer(TimeSpan.FromSeconds(2));
        var start = new DateTimeOffset(2026, 3, 23, 12, 0, 0, TimeSpan.Zero);

        _ = debouncer.Observe("fortniteclient-win64-shipping", start);
        _ = debouncer.Observe("fortniteclient-win64-shipping", start.AddSeconds(2));

        var firstSwitch = debouncer.Observe("cs2.exe", start.AddSeconds(3));
        var switched = debouncer.Observe("cs2", start.AddSeconds(5));

        Assert.Equal("fortniteclient-win64-shipping", firstSwitch.StableExecutable);
        Assert.False(firstSwitch.Changed);
        Assert.Equal("cs2", switched.StableExecutable);
        Assert.True(switched.Changed);
    }
}
