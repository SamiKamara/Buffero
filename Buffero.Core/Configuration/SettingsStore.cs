using System.Text.Json;

namespace Buffero.Core.Configuration;

public sealed class SettingsStore
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettings LoadOrCreate(
        string path,
        Func<AppSettings> defaultsFactory,
        string? fallbackFfmpegPath = null,
        int? estimateSourceWidth = null,
        int? estimateSourceHeight = null)
    {
        if (!File.Exists(path))
        {
            var defaults = defaultsFactory();
            defaults.Normalize(fallbackFfmpegPath, estimateSourceWidth, estimateSourceHeight);
            Save(path, defaults, estimateSourceWidth, estimateSourceHeight);
            return defaults;
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, _serializerOptions) ?? defaultsFactory();
        settings.Normalize(fallbackFfmpegPath, estimateSourceWidth, estimateSourceHeight);
        Save(path, settings, estimateSourceWidth, estimateSourceHeight);
        return settings;
    }

    public void Save(
        string path,
        AppSettings settings,
        int? estimateSourceWidth = null,
        int? estimateSourceHeight = null)
    {
        settings.Normalize(settings.FfmpegPath, estimateSourceWidth, estimateSourceHeight);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(settings, _serializerOptions);
        File.WriteAllText(path, json);
    }
}
