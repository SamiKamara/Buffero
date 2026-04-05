using Buffero.Core.Configuration;
using Buffero.Core.Capture;

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
        Assert.Equal(UiMode.Default, settings.UiMode);
        Assert.Equal(AppSettings.DefaultModeDefaultWindowWidth, settings.DefaultModeWindowWidth);
        Assert.Equal(AppSettings.DefaultModeDefaultWindowHeight, settings.DefaultModeWindowHeight);
        Assert.Equal(AppSettings.AdvancedModeDefaultWindowWidth, settings.AdvancedModeWindowWidth);
        Assert.Equal(AppSettings.AdvancedModeDefaultWindowHeight, settings.AdvancedModeWindowHeight);
        Assert.Equal("ffmpeg.exe", settings.FfmpegPath);
        Assert.Equal(CaptureBackend.Native, settings.CaptureBackend);
        Assert.True(settings.ReplayBufferEnabled);
        Assert.Equal(BufferActivationMode.Automatic, settings.BufferActivationMode);
        Assert.Equal(QualityInputMode.Bitrate, settings.QualityInputMode);
        Assert.Equal(6, settings.QualityBitrateMbps);
        Assert.True(settings.ToggleBufferHotkey.Alt);
        Assert.Equal("L", settings.ToggleBufferHotkey.Key);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsOutputResolution()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "buffero-tests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var store = new SettingsStore();
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.ReplayBufferEnabled = false;
        settings.UiMode = UiMode.Advanced;
        settings.DefaultModeWindowWidth = 860;
        settings.DefaultModeWindowHeight = 660;
        settings.AdvancedModeWindowWidth = 1260;
        settings.AdvancedModeWindowHeight = 900;
        settings.CaptureBackend = CaptureBackend.Ffmpeg;
        settings.CaptureMode = CaptureMode.Display;
        settings.OutputResolution = OutputResolutionMode.Max720p;
        settings.NotificationsEnabled = false;
        settings.BufferActivationMode = BufferActivationMode.HotkeyToggle;
        settings.ToggleBufferHotkey = new HotkeyBinding
        {
            Ctrl = true,
            Alt = false,
            Key = "L"
        };
        settings.QualityInputMode = QualityInputMode.Bitrate;
        settings.QualityBitrateMbps = 16;

        store.Save(settingsPath, settings);
        var loaded = store.LoadOrCreate(settingsPath, () => AppSettings.CreateDefault("fallback.exe"), "ffmpeg.exe");

        Assert.False(loaded.ReplayBufferEnabled);
        Assert.Equal(UiMode.Advanced, loaded.UiMode);
        Assert.Equal(860, loaded.DefaultModeWindowWidth);
        Assert.Equal(660, loaded.DefaultModeWindowHeight);
        Assert.Equal(1260, loaded.AdvancedModeWindowWidth);
        Assert.Equal(900, loaded.AdvancedModeWindowHeight);
        Assert.Equal(CaptureBackend.Ffmpeg, loaded.CaptureBackend);
        Assert.Equal(CaptureMode.Display, loaded.CaptureMode);
        Assert.Equal(OutputResolutionMode.Max720p, loaded.OutputResolution);
        Assert.False(loaded.NotificationsEnabled);
        Assert.Equal(BufferActivationMode.HotkeyToggle, loaded.BufferActivationMode);
        Assert.True(loaded.ToggleBufferHotkey.Ctrl);
        Assert.False(loaded.ToggleBufferHotkey.Alt);
        Assert.Equal("L", loaded.ToggleBufferHotkey.Key);
        Assert.Equal(QualityInputMode.Bitrate, loaded.QualityInputMode);
        Assert.Equal(16, loaded.QualityBitrateMbps);
        Assert.Contains("\"replayBufferEnabled\": false", File.ReadAllText(settingsPath));
        Assert.Contains("\"uiMode\": \"Advanced\"", File.ReadAllText(settingsPath));
        Assert.Contains("\"defaultModeWindowWidth\": 860", File.ReadAllText(settingsPath));
        Assert.Contains("\"defaultModeWindowHeight\": 660", File.ReadAllText(settingsPath));
        Assert.Contains("\"advancedModeWindowWidth\": 1260", File.ReadAllText(settingsPath));
        Assert.Contains("\"advancedModeWindowHeight\": 900", File.ReadAllText(settingsPath));
        Assert.Contains("\"captureBackend\": \"Ffmpeg\"", File.ReadAllText(settingsPath));
        Assert.Contains("\"captureMode\": \"Display\"", File.ReadAllText(settingsPath));
        Assert.Contains("\"outputResolution\": \"Max720p\"", File.ReadAllText(settingsPath));
        Assert.Contains("\"notificationsEnabled\": false", File.ReadAllText(settingsPath));
        Assert.Contains("\"bufferActivationMode\": \"HotkeyToggle\"", File.ReadAllText(settingsPath));
        Assert.Contains("\"toggleBufferHotkey\": {", File.ReadAllText(settingsPath));
        Assert.Contains("\"qualityInputMode\": \"Bitrate\"", File.ReadAllText(settingsPath));
        Assert.Contains("\"qualityBitrateMbps\": 16", File.ReadAllText(settingsPath));
    }

    [Fact]
    public void Normalize_ResetsInvalidCaptureSettingsToDefaults()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.CaptureBackend = (CaptureBackend)999;
        settings.CaptureMode = (CaptureMode)999;

        settings.Normalize();

        Assert.Equal(CaptureBackend.Native, settings.CaptureBackend);
        Assert.Equal(CaptureMode.Window, settings.CaptureMode);
    }

    [Fact]
    public void Normalize_ClampsReplaySecondsToUpdatedMaximum()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.BufferSeconds = 9_999;

        settings.Normalize();

        Assert.Equal(AppSettings.MaxBufferSeconds, settings.BufferSeconds);
    }

    [Fact]
    public void Normalize_UpdatesBitrateEstimate_WhenCrfIsActive()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.OutputResolution = OutputResolutionMode.Max720p;
        settings.Fps = 60;
        settings.QualityInputMode = QualityInputMode.Crf;
        settings.QualityCrf = 20;
        settings.QualityBitrateMbps = 99;

        settings.Normalize();

        Assert.Equal(
            CaptureQualityEstimator.EstimateBitrateMbps(OutputResolutionMode.Max720p, 60, 20),
            settings.QualityBitrateMbps);
    }

    [Fact]
    public void Normalize_UsesProvidedResolution_WhenEstimatingBitrate()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.OutputResolution = OutputResolutionMode.Native;
        settings.Fps = 60;
        settings.QualityInputMode = QualityInputMode.Crf;
        settings.QualityCrf = 20;

        settings.Normalize(null, 2560, 1440);

        Assert.Equal(
            CaptureQualityEstimator.EstimateBitrateMbps(2560, 1440, 60, 20),
            settings.QualityBitrateMbps);
    }

    [Fact]
    public void Normalize_UpdatesCrfEstimate_WhenBitrateIsActive()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.OutputResolution = OutputResolutionMode.Max1080p;
        settings.Fps = 30;
        settings.QualityInputMode = QualityInputMode.Bitrate;
        settings.QualityBitrateMbps = 12;
        settings.QualityCrf = 35;

        settings.Normalize();

        Assert.Equal(
            CaptureQualityEstimator.EstimateCrf(OutputResolutionMode.Max1080p, 30, 12),
            settings.QualityCrf);
    }

    [Fact]
    public void Normalize_UsesProvidedResolutionWithOutputCap_WhenEstimatingCrf()
    {
        var settings = AppSettings.CreateDefault("ffmpeg.exe");
        settings.OutputResolution = OutputResolutionMode.Max1080p;
        settings.Fps = 30;
        settings.QualityInputMode = QualityInputMode.Bitrate;
        settings.QualityBitrateMbps = 12;

        settings.Normalize(null, 2560, 1440);

        Assert.Equal(
            CaptureQualityEstimator.EstimateCrf(1920, 1080, 30, CaptureQualityEstimator.MegabitsPerSecondToBitsPerSecond(12)),
            settings.QualityCrf);
    }
}
