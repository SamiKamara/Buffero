namespace Buffero.Core.Configuration;

public sealed record BufferoPaths(
    string RootDirectory,
    string SettingsFilePath,
    string TempSessionsDirectory,
    string LogsDirectory)
{
    public static BufferoPaths Create(string appName = "Buffero")
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName);

        return new BufferoPaths(
            root,
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "temp", "segments"),
            Path.Combine(root, "logs"));
    }

    public void EnsureDirectories(string? saveDirectory = null)
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
        Directory.CreateDirectory(TempSessionsDirectory);
        Directory.CreateDirectory(LogsDirectory);

        if (!string.IsNullOrWhiteSpace(saveDirectory))
        {
            Directory.CreateDirectory(saveDirectory);
        }
    }
}
