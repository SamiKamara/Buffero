using Buffero.Core.State;

namespace Buffero.Tests;

public sealed class ReplayStateMachineTests
{
    [Fact]
    public void Transitions_ToArmed_WhenTargetIsEligible()
    {
        var machine = new ReplayStateMachine();

        machine.SetEligibleTarget("cs2");

        Assert.Equal(ReplayState.Armed, machine.CurrentState);
    }

    [Fact]
    public void Transitions_ToExporting_WhenCaptureAndExportOverlap()
    {
        var machine = new ReplayStateMachine();
        machine.MarkCaptureStarted("valorant");

        machine.MarkExportQueued();

        Assert.Equal(ReplayState.Exporting, machine.CurrentState);
    }
}
