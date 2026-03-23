using Buffero.Core.Configuration;

namespace Buffero.Core.Detection;

public sealed record GameMatchResult(bool IsMatch, string? MatchedExecutable);

public sealed class GameMatchEvaluator
{
    public GameMatchResult Evaluate(IEnumerable<string> runningProcessNames, string? foregroundProcessName, AppSettings settings)
    {
        var allowList = settings.AllowedExecutables
            .Select(HotkeyBinding.NormalizeExecutableToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allowList.Count == 0)
        {
            return new GameMatchResult(false, null);
        }

        var foreground = HotkeyBinding.NormalizeExecutableToken(foregroundProcessName);

        if (settings.RequireForegroundWindow)
        {
            return allowList.Contains(foreground)
                ? new GameMatchResult(true, foreground)
                : new GameMatchResult(false, null);
        }

        foreach (var processName in runningProcessNames.Select(HotkeyBinding.NormalizeExecutableToken))
        {
            if (allowList.Contains(processName))
            {
                return new GameMatchResult(true, processName);
            }
        }

        return new GameMatchResult(false, null);
    }
}
