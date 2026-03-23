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
        Assert.True(settings.ReplayBufferEnabled);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsOutputResolution()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "buffero-tests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var store = new SettingsStore();
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.ReplayBufferEnabled = false;
        settings.CaptureMode = CaptureMode.Display;
        settings.OutputResolution = OutputResolutionMode.Max720p;
        settings.NotificationsEnabled = false;

        store.Save(settingsPath, settings);
        var loaded = store.LoadOrCreate(settingsPath, () => AppSettings.CreateDefault("fallback.exe"), "ffmpeg.exe");

        Assert.False(loaded.ReplayBufferEnabled);
        Assert.Equal(CaptureMode.Display, loaded.CaptureMode);
        Assert.Equal(OutputResolutionMode.Max720p, loaded.OutputResolution);
        Assert.False(loaded.NotificationsEnabled);
        Assert.Contains("\"replayBufferEnabled\": false", File.ReadAllText(settingsPath));
        Assert.Contains("\"captureMode\": \"Display\"", File.ReadAllText(settingsPath));
        Assert.Contains("\"outputResolution\": \"Max720p\"", File.ReadAllText(settingsPath));
        Assert.Contains("\"notificationsEnabled\": false", File.ReadAllText(settingsPath));
    }

    [Fact]
    public void Normalize_ResetsInvalidCaptureModeToWindow()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.CaptureMode = (CaptureMode)999;

        settings.Normalize();

        Assert.Equal(CaptureMode.Window, settings.CaptureMode);
    }
}
