using Buffero.Core.Capture;

namespace Buffero.Tests;

public sealed class ClipNameBuilderTests
{
    [Fact]
    public void Build_ReplacesTokens_AndAppendsExtension()
    {
        var timestamp = new DateTimeOffset(2026, 3, 23, 20, 0, 5, TimeSpan.FromHours(2));
        var expectedTimestamp = timestamp.ToLocalTime();

        var fileName = ClipNameBuilder.Build("clip-{date}-{time}-{game}", timestamp, "My Game");

        Assert.Equal(
            $"clip-{expectedTimestamp:yyyyMMdd}-{expectedTimestamp:HHmmss}-My-Game.mp4",
            fileName);
    }

    [Fact]
    public void Build_SanitizesInvalidCharacters()
    {
        var timestamp = new DateTimeOffset(2026, 3, 23, 20, 0, 5, TimeSpan.Zero);

        var fileName = ClipNameBuilder.Build("clip:{game}", timestamp, "bad/name");

        Assert.DoesNotContain(':', fileName);
        Assert.DoesNotContain('/', fileName);
    }
}
