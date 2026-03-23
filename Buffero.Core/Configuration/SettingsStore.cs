using System.Text.Json;

namespace Buffero.Core.Configuration;

public sealed class SettingsStore
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettings LoadOrCreate(string path, Func<AppSettings> defaultsFactory, string? fallbackFfmpegPath = null)
    {
        if (!File.Exists(path))
        {
            var defaults = defaultsFactory();
            defaults.Normalize(fallbackFfmpegPath);
            Save(path, defaults);
            return defaults;
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, _serializerOptions) ?? defaultsFactory();
        settings.Normalize(fallbackFfmpegPath);
        Save(path, settings);
        return settings;
    }

    public void Save(string path, AppSettings settings)
    {
        settings.Normalize(settings.FfmpegPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(settings, _serializerOptions);
        File.WriteAllText(path, json);
    }
}
