using System.Text.Json.Serialization;

namespace Buffero.Core.Configuration;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }

    public bool AutoStartEnabled { get; set; } = true;

    public bool RequireForegroundWindow { get; set; } = true;

    public string SaveDirectory { get; set; } = string.Empty;

    public int BufferSeconds { get; set; } = 30;

    public int SegmentSeconds { get; set; } = 2;

    public int Fps { get; set; } = 30;

    public int QualityCrf { get; set; } = 23;

    public int MaxTempStorageGb { get; set; } = 4;

    public HotkeyBinding SaveReplayHotkey { get; set; } = HotkeyBinding.Default;

    public string FfmpegPath { get; set; } = string.Empty;

    public bool IncludeSystemAudio { get; set; }

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

    public void Normalize(string? fallbackFfmpegPath = null)
    {
        SaveDirectory = string.IsNullOrWhiteSpace(SaveDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Buffero Videos")
            : SaveDirectory.Trim();

        BufferSeconds = Math.Clamp(BufferSeconds, 15, 120);
        SegmentSeconds = Math.Clamp(SegmentSeconds, 1, 10);
        Fps = Math.Clamp(Fps, 15, 60);
        QualityCrf = Math.Clamp(QualityCrf, 18, 35);
        MaxTempStorageGb = Math.Clamp(MaxTempStorageGb, 1, 32);
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
}
