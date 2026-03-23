using Buffero.Core.Configuration;
using Buffero.Core.Detection;

namespace Buffero.Tests;

public sealed class GameMatchEvaluatorTests
{
    [Fact]
    public void Evaluate_UsesForegroundWhenRequired()
    {
        var evaluator = new GameMatchEvaluator();
        var settings = new AppSettings
        {
            RequireForegroundWindow = true,
            AllowedExecutables = ["valorant.exe"]
        };

        var result = evaluator.Evaluate(["valorant", "discord"], "discord", settings);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Evaluate_FindsRunningProcessWhenForegroundNotRequired()
    {
        var evaluator = new GameMatchEvaluator();
        var settings = new AppSettings
        {
            RequireForegroundWindow = false,
            AllowedExecutables = ["cs2.exe"]
        };

        var result = evaluator.Evaluate(["steam", "cs2"], null, settings);

        Assert.True(result.IsMatch);
        Assert.Equal("cs2", result.MatchedExecutable);
    }
}
