using System.Diagnostics;

namespace Buffero.App.Infrastructure;

public sealed class FfmpegLocator
{
    public string FindBestPath()
    {
        foreach (var candidate in GetCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> GetCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe");

        var packageRoot = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(packageRoot))
        {
            foreach (var path in Directory.EnumerateFiles(packageRoot, "ffmpeg.exe", SearchOption.AllDirectories))
            {
                yield return path;
            }
        }

        foreach (var pathEntry in ProbePathForFfmpeg())
        {
            yield return pathEntry;
        }
    }

    private static IEnumerable<string> ProbePathForFfmpeg()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "ffmpeg",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            return output
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch
        {
            return [];
        }
    }
}
