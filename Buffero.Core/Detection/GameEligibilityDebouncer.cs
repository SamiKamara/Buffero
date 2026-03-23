using Buffero.Core.Configuration;

namespace Buffero.Core.Detection;

public sealed record DebouncedGameMatch(string? StableExecutable, bool Changed);

public sealed class GameEligibilityDebouncer
{
    private readonly TimeSpan _debounceInterval;
    private bool _hasPendingObservation;
    private string? _stableExecutable;
    private string? _pendingExecutable;
    private DateTimeOffset? _pendingSince;

    public GameEligibilityDebouncer(TimeSpan debounceInterval)
    {
        if (debounceInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceInterval), "Debounce interval must be positive.");
        }

        _debounceInterval = debounceInterval;
    }

    public string? StableExecutable => _stableExecutable;

    public DebouncedGameMatch Observe(string? executable, DateTimeOffset observedAt)
    {
        var normalized = NormalizeExecutable(executable);

        if (string.Equals(normalized, _stableExecutable, StringComparison.OrdinalIgnoreCase))
        {
            ClearPending();
            return new DebouncedGameMatch(_stableExecutable, Changed: false);
        }

        if (!_hasPendingObservation
            || !string.Equals(normalized, _pendingExecutable, StringComparison.OrdinalIgnoreCase))
        {
            _pendingExecutable = normalized;
            _pendingSince = observedAt;
            _hasPendingObservation = true;
            return new DebouncedGameMatch(_stableExecutable, Changed: false);
        }

        if (_pendingSince is null || observedAt - _pendingSince.Value < _debounceInterval)
        {
            return new DebouncedGameMatch(_stableExecutable, Changed: false);
        }

        var changed = !string.Equals(_stableExecutable, normalized, StringComparison.OrdinalIgnoreCase);
        _stableExecutable = normalized;
        ClearPending();
        return new DebouncedGameMatch(_stableExecutable, changed);
    }

    public void Reset()
    {
        _stableExecutable = null;
        ClearPending();
    }

    private void ClearPending()
    {
        _hasPendingObservation = false;
        _pendingExecutable = null;
        _pendingSince = null;
    }

    private static string? NormalizeExecutable(string? executable)
    {
        var normalized = HotkeyBinding.NormalizeExecutableToken(executable);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
