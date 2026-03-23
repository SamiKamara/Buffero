namespace Buffero.Core.State;

public sealed class ReplayStateMachine
{
    private bool _eligibleTargetDetected;
    private bool _captureActive;
    private bool _recovering;
    private int _exportJobs;
    private string? _activeTarget;
    private string? _faultReason;
    private string? _lastMessage;

    public ReplayState CurrentState { get; private set; } = ReplayState.Idle;

    public string StatusMessage => _lastMessage ?? "Idle";

    public event Action<ReplayState, string>? Changed;

    public void SetEligibleTarget(string? targetName)
    {
        _eligibleTargetDetected = !string.IsNullOrWhiteSpace(targetName);
        _activeTarget = targetName;
        Recompute();
    }

    public void MarkCaptureStarted(string? targetName)
    {
        _captureActive = true;
        _recovering = false;
        _faultReason = null;
        _activeTarget = targetName ?? _activeTarget;
        Recompute();
    }

    public void MarkCaptureStopped()
    {
        _captureActive = false;
        _recovering = false;
        _faultReason = null;
        Recompute();
    }

    public void MarkExportQueued()
    {
        _exportJobs++;
        Recompute();
    }

    public void MarkExportCompleted()
    {
        _exportJobs = Math.Max(0, _exportJobs - 1);
        Recompute();
    }

    public void MarkRecovering(string reason)
    {
        _recovering = true;
        _faultReason = reason;
        Recompute();
    }

    public void MarkRecovered()
    {
        _recovering = false;
        _faultReason = null;
        Recompute();
    }

    public void MarkFault(string reason)
    {
        _faultReason = reason;
        _recovering = false;
        _captureActive = false;
        Recompute();
    }

    public void ClearFault()
    {
        _faultReason = null;
        Recompute();
    }

    private void Recompute()
    {
        var nextState = ComputeState();
        var message = BuildMessage(nextState);

        if (nextState == CurrentState && string.Equals(message, _lastMessage, StringComparison.Ordinal))
        {
            return;
        }

        CurrentState = nextState;
        _lastMessage = message;
        Changed?.Invoke(CurrentState, message);
    }

    private ReplayState ComputeState()
    {
        if (!string.IsNullOrWhiteSpace(_faultReason) && !_recovering && !_captureActive)
        {
            return ReplayState.Faulted;
        }

        if (_recovering)
        {
            return ReplayState.Recovering;
        }

        if (_captureActive && _exportJobs > 0)
        {
            return ReplayState.Exporting;
        }

        if (_captureActive)
        {
            return ReplayState.Capturing;
        }

        if (_eligibleTargetDetected)
        {
            return ReplayState.Armed;
        }

        return ReplayState.Idle;
    }

    private string BuildMessage(ReplayState state)
    {
        return state switch
        {
            ReplayState.Idle => "Idle. Waiting for an eligible game.",
            ReplayState.Armed => $"Armed for {(_activeTarget ?? "configured game")}.",
            ReplayState.Capturing => $"Buffering {(_activeTarget ?? "desktop")}…",
            ReplayState.Exporting => "Saving replay while buffering continues.",
            ReplayState.Recovering => $"Recovering: {_faultReason}",
            ReplayState.Faulted => $"Faulted: {_faultReason}",
            _ => "Idle. Waiting for an eligible game."
        };
    }
}
