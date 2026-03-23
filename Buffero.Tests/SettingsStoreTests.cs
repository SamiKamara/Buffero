using Buffero.Core.Configuration;

namespace Buffero.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void LoadOrCreate_CreatesDefaultsWhenFileMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "buffero-tests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var store = new SettingsStore();

        var settings = store.LoadOrCreate(settingsPath, () => AppSettings.CreateDefault("ffmpeg.exe"), "ffmpeg.exe");

        Assert.True(File.Exists(settingsPath));
        Assert.Equal("ffmpeg.exe", settings.FfmpegPath);
    }
}
