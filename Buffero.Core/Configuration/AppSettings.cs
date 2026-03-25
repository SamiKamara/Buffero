using System.Text.Json.Serialization;
using Buffero.Core.Capture;

namespace Buffero.Core.Configuration;

public sealed class AppSettings
{
    public const double DefaultModeDefaultWindowWidth = 920;
    public const double DefaultModeDefaultWindowHeight = 700;
    public const double DefaultModeMinWindowWidth = 820;
    public const double DefaultModeMinWindowHeight = 640;
    public const double AdvancedModeDefaultWindowWidth = 1120;
    public const double AdvancedModeDefaultWindowHeight = 840;
    public const double AdvancedModeMinWindowWidth = 960;
    public const double AdvancedModeMinWindowHeight = 720;
    private const double MaxWindowWidth = 3200;
    private const double MaxWindowHeight = 2400;

    public UiMode UiMode { get; set; } = global::Buffero.Core.Configuration.UiMode.Default;

    public double DefaultModeWindowWidth { get; set; } = DefaultModeDefaultWindowWidth;

    public double DefaultModeWindowHeight { get; set; } = DefaultModeDefaultWindowHeight;

    public double AdvancedModeWindowWidth { get; set; } = AdvancedModeDefaultWindowWidth;

    public double AdvancedModeWindowHeight { get; set; } = AdvancedModeDefaultWindowHeight;

    public bool ReplayBufferEnabled { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool AutoStartEnabled { get; set; } = true;

    public bool RequireForegroundWindow { get; set; } = true;

    public string SaveDirectory { get; set; } = string.Empty;

    public int BufferSeconds { get; set; } = 30;

    public int SegmentSeconds { get; set; } = 2;

    public int Fps { get; set; } = 30;

    public int QualityCrf { get; set; } = CaptureQualityEstimator.EstimateCrf(OutputResolutionMode.Native, 30, 6);

    public QualityInputMode QualityInputMode { get; set; } = QualityInputMode.Bitrate;

    public int QualityBitrateMbps { get; set; } = 6;

    public int MaxTempStorageGb { get; set; } = 4;

    public HotkeyBinding SaveReplayHotkey { get; set; } = HotkeyBinding.Default;

    public string FfmpegPath { get; set; } = string.Empty;

    public bool IncludeSystemAudio { get; set; }

    public bool NotificationsEnabled { get; set; } = true;

    public CaptureBackend CaptureBackend { get; set; } = CaptureBackend.Native;

    public CaptureMode CaptureMode { get; set; } = CaptureMode.Window;

    public OutputResolutionMode OutputResolution { get; set; } = OutputResolutionMode.Native;

    public string ClipFilePattern { get; set; } = "Buffero-{timestamp}-{game}";

    public List<string> AllowedExecutables { get; set; } = [];

    [JsonIgnore]
    public long MaxTempStorageBytes => Math.Max(1, MaxTempStorageGb) * 1024L * 1024L * 1024L;

    public static AppSettings CreateDefault(string ffmpegPath)
    {
        return new AppSettings
        {
            SaveDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "Buffero Videos"),
            FfmpegPath = ffmpegPath,
            AllowedExecutables = ["valorant", "cs2", "fortniteclient-win64-shipping", "leagueclientux"]
        };
    }

    public void Normalize(
        string? fallbackFfmpegPath = null,
        int? estimateSourceWidth = null,
        int? estimateSourceHeight = null)
    {
        SaveDirectory = string.IsNullOrWhiteSpace(SaveDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Buffero Videos")
            : SaveDirectory.Trim();

        UiMode = Enum.IsDefined(UiMode)
            ? UiMode
            : global::Buffero.Core.Configuration.UiMode.Default;
        DefaultModeWindowWidth = ClampWindowDimension(
            DefaultModeWindowWidth,
            DefaultModeMinWindowWidth,
            MaxWindowWidth,
            DefaultModeDefaultWindowWidth);
        DefaultModeWindowHeight = ClampWindowDimension(
            DefaultModeWindowHeight,
            DefaultModeMinWindowHeight,
            MaxWindowHeight,
            DefaultModeDefaultWindowHeight);
        AdvancedModeWindowWidth = ClampWindowDimension(
            AdvancedModeWindowWidth,
            AdvancedModeMinWindowWidth,
            MaxWindowWidth,
            AdvancedModeDefaultWindowWidth);
        AdvancedModeWindowHeight = ClampWindowDimension(
            AdvancedModeWindowHeight,
            AdvancedModeMinWindowHeight,
            MaxWindowHeight,
            AdvancedModeDefaultWindowHeight);
        BufferSeconds = Math.Clamp(BufferSeconds, 15, 120);
        SegmentSeconds = Math.Clamp(SegmentSeconds, 1, 10);
        Fps = CaptureQualityEstimator.ClampFps(Fps);
        QualityCrf = CaptureQualityEstimator.ClampCrf(QualityCrf);
        QualityBitrateMbps = CaptureQualityEstimator.ClampBitrateMbps(QualityBitrateMbps);
        MaxTempStorageGb = Math.Clamp(MaxTempStorageGb, 1, 32);
        CaptureBackend = Enum.IsDefined(CaptureBackend)
            ? CaptureBackend
            : CaptureBackend.Native;
        CaptureMode = Enum.IsDefined(CaptureMode)
            ? CaptureMode
            : CaptureMode.Window;
        OutputResolution = Enum.IsDefined(OutputResolution)
            ? OutputResolution
            : OutputResolutionMode.Native;
        QualityInputMode = Enum.IsDefined(QualityInputMode)
            ? QualityInputMode
            : QualityInputMode.Crf;
        var (estimateWidth, estimateHeight) = CaptureQualityEstimator.GetEstimatedOutputSize(
            OutputResolution,
            estimateSourceWidth,
            estimateSourceHeight);
        if (QualityInputMode == QualityInputMode.Bitrate)
        {
            QualityCrf = CaptureQualityEstimator.EstimateCrf(
                estimateWidth,
                estimateHeight,
                Fps,
                CaptureQualityEstimator.MegabitsPerSecondToBitsPerSecond(QualityBitrateMbps));
        }
        else
        {
            QualityBitrateMbps = CaptureQualityEstimator.EstimateBitrateMbps(
                estimateWidth,
                estimateHeight,
                Fps,
                QualityCrf);
        }
        ClipFilePattern = string.IsNullOrWhiteSpace(ClipFilePattern)
            ? "Buffero-{timestamp}-{game}"
            : ClipFilePattern.Trim();
        FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath)
            ? fallbackFfmpegPath ?? string.Empty
            : FfmpegPath.Trim();
        SaveReplayHotkey ??= HotkeyBinding.Default;
        SaveReplayHotkey.Normalize();
        AllowedExecutables ??= [];
        AllowedExecutables = AllowedExecutables
            .Select(HotkeyBinding.NormalizeExecutableToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public (double Width, double Height) GetWindowSize(UiMode uiMode)
    {
        return uiMode switch
        {
            global::Buffero.Core.Configuration.UiMode.Advanced => (AdvancedModeWindowWidth, AdvancedModeWindowHeight),
            _ => (DefaultModeWindowWidth, DefaultModeWindowHeight)
        };
    }

    public static (double Width, double Height) GetMinimumWindowSize(UiMode uiMode)
    {
        return uiMode switch
        {
            global::Buffero.Core.Configuration.UiMode.Advanced => (AdvancedModeMinWindowWidth, AdvancedModeMinWindowHeight),
            _ => (DefaultModeMinWindowWidth, DefaultModeMinWindowHeight)
        };
    }

    private static double ClampWindowDimension(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(Math.Round(value), min, max);
    }
}
