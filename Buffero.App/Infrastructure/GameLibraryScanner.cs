using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Buffero.Core.Configuration;

namespace Buffero.App.Infrastructure;

public sealed class GameLibraryScanner
{
    private static readonly Regex SteamPathRegex = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex SteamInstallDirRegex = new("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex NameTokenRegex = new("[a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly FileLogger _logger;

    public GameLibraryScanner(FileLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> ScanAndMerge(AppSettings settings)
    {
        var existing = settings.AllowedExecutables
            .Select(HotkeyBinding.NormalizeExecutableToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = new List<string>();

        foreach (var candidate in DiscoverAllExecutables())
        {
            if (existing.Add(candidate))
            {
                settings.AllowedExecutables.Add(candidate);
                added.Add(candidate);
            }
        }

        if (added.Count > 0)
        {
            _logger.Info($"Game library scan added {added.Count} executable(s): {string.Join(", ", added.OrderBy(value => value))}");
        }
        else
        {
            _logger.Info("Game library scan found no new executables.");
        }

        settings.Normalize(settings.FfmpegPath);
        return added;
    }

    private IEnumerable<string> DiscoverAllExecutables()
    {
        return DiscoverSteamExecutables()
            .Concat(DiscoverEpicExecutables())
            .Concat(DiscoverCommonInstallExecutables())
            .Select(HotkeyBinding.NormalizeExecutableToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token);
    }

    private IEnumerable<string> DiscoverSteamExecutables()
    {
        var steamRoot = FindSteamRoot();
        if (string.IsNullOrWhiteSpace(steamRoot))
        {
            yield break;
        }

        var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile))
        {
            yield break;
        }

        string content;
        try
        {
            content = File.ReadAllText(libraryFile);
        }
        catch
        {
            yield break;
        }

        var libraries = SteamPathRegex.Matches(content)
            .Select(match => match.Groups[1].Value.Replace(@"\\", @"\"))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var library in libraries)
        {
            var steamAppsDirectory = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamAppsDirectory))
            {
                continue;
            }

            IEnumerable<string> manifests;

            try
            {
                manifests = Directory.EnumerateFiles(steamAppsDirectory, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var manifest in manifests)
            {
                string manifestContent;

                try
                {
                    manifestContent = File.ReadAllText(manifest);
                }
                catch
                {
                    continue;
                }

                var installDirMatch = SteamInstallDirRegex.Match(manifestContent);
                if (!installDirMatch.Success)
                {
                    continue;
                }

                var installDirectory = Path.Combine(steamAppsDirectory, "common", installDirMatch.Groups[1].Value);
                foreach (var executable in CollectLikelyGameExecutables(installDirectory))
                {
                    yield return executable;
                }
            }
        }
    }

    private IEnumerable<string> DiscoverEpicExecutables()
    {
        var manifestsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic",
            "EpicGamesLauncher",
            "Data",
            "Manifests");

        if (!Directory.Exists(manifestsRoot))
        {
            yield break;
        }

        IEnumerable<string> manifestPaths;

        try
        {
            manifestPaths = Directory.EnumerateFiles(manifestsRoot, "*.item", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var manifestPath in manifestPaths)
        {
            string content;

            try
            {
                content = File.ReadAllText(manifestPath);
            }
            catch
            {
                continue;
            }

            var discoveredExecutables = new List<string>();

            try
            {
                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;

                var installDirectory = root.TryGetProperty("InstallLocation", out var installElement)
                    ? installElement.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(installDirectory))
                {
                    discoveredExecutables.AddRange(CollectLikelyGameExecutables(installDirectory));
                }
            }
            catch
            {
                // Skip malformed Epic manifests.
            }

            foreach (var executable in discoveredExecutables)
            {
                yield return executable;
            }
        }
    }

    private IEnumerable<string> DiscoverCommonInstallExecutables()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
        {
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "Games"));
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "Epic Games"));
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "GOG Games"));
        }

        AddIfPresent(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games"));
        AddIfPresent(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Origin Games"));
        AddIfPresent(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ubisoft", "Ubisoft Game Launcher", "games"));
        AddIfPresent(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher", "games"));
        AddIfPresent(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GOG Galaxy", "Games"));
        AddIfPresent(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "Games"));

        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> gameDirectories;

            try
            {
                gameDirectories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var gameDirectory in gameDirectories)
            {
                foreach (var executable in CollectLikelyGameExecutables(gameDirectory))
                {
                    yield return executable;
                }
            }
        }
    }

    private static IEnumerable<string> CollectLikelyGameExecutables(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return [];
        }

        var referenceProfile = BuildReferenceProfile(installDirectory);
        var candidates = new List<ExecutableCandidate>();

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(installDirectory, "*.exe", new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         IgnoreInaccessible = true
                     }))
            {
                var relativePath = Path.GetRelativePath(installDirectory, filePath);
                var relativePathLower = relativePath.ToLowerInvariant();
                var stem = HotkeyBinding.NormalizeExecutableToken(Path.GetFileNameWithoutExtension(filePath));

                if (ShouldIgnoreExecutable(stem, relativePathLower))
                {
                    continue;
                }

                long sizeBytes;
                try
                {
                    sizeBytes = new FileInfo(filePath).Length;
                }
                catch
                {
                    sizeBytes = 0;
                }

                var score = ScoreExecutable(relativePathLower, stem, sizeBytes, referenceProfile);
                if (score <= 0)
                {
                    continue;
                }

                candidates.Add(new ExecutableCandidate(stem, score, sizeBytes));
            }
        }
        catch
        {
            return [];
        }

        var rankedCandidates = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.SizeBytes)
            .ThenBy(candidate => candidate.Stem)
            .ToArray();

        if (rankedCandidates.Length == 0)
        {
            return [];
        }

        var scoreThreshold = Math.Max(5, rankedCandidates[0].Score - 3);
        var selected = rankedCandidates
            .Where(candidate => candidate.Score >= scoreThreshold)
            .Select(candidate => candidate.Stem)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        if (selected.Length > 0)
        {
            return selected;
        }

        return [rankedCandidates[0].Stem];
    }

    private static int ScoreExecutable(
        string relativePathLower,
        string stem,
        long sizeBytes,
        ReferenceProfile referenceProfile)
    {
        var score = 0;

        if (!relativePathLower.Contains(Path.DirectorySeparatorChar)
            && !relativePathLower.Contains(Path.AltDirectorySeparatorChar))
        {
            score += 3;
        }

        if (relativePathLower.Contains(@"bin64")
            || relativePathLower.Contains(@"binaries\win64")
            || relativePathLower.Contains(@"game\client")
            || relativePathLower.Contains(@"win64")
            || relativePathLower.Contains(@"x64"))
        {
            score += 2;
        }

        if (stem.Contains("shipping", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("64", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("x64", StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }

        score += ScoreReferenceMatch(stem, referenceProfile);

        if (sizeBytes >= 10 * 1024 * 1024)
        {
            score += 2;
        }
        else if (sizeBytes >= 3 * 1024 * 1024)
        {
            score += 1;
        }

        return score;
    }

    private static int ScoreReferenceMatch(string stem, ReferenceProfile referenceProfile)
    {
        if (referenceProfile.NormalizedName.Length > 0)
        {
            if (string.Equals(stem, referenceProfile.NormalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return 5;
            }

            if (stem.Contains(referenceProfile.NormalizedName, StringComparison.OrdinalIgnoreCase)
                || referenceProfile.NormalizedName.Contains(stem, StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }
        }

        var tokenMatches = referenceProfile.SignificantTokens.Count(token =>
            stem.Contains(token, StringComparison.OrdinalIgnoreCase));
        if (tokenMatches >= 2)
        {
            return 3;
        }

        if (tokenMatches == 1)
        {
            return 2;
        }

        return referenceProfile.Acronyms.Any(acronym =>
            stem.Contains(acronym, StringComparison.OrdinalIgnoreCase))
            ? 1
            : 0;
    }

    private static bool ShouldIgnoreExecutable(string stem, string relativePathLower)
    {
        string[] ignoredPathTokens =
        [
            "_commonredist",
            "redistributable",
            "redist",
            "support",
            @"battleye\",
            @"easyanticheat\",
            @"launcher\portal\",
            @"tools\"
        ];

        if (ignoredPathTokens.Any(relativePathLower.Contains))
        {
            return true;
        }

        string[] ignoredStemTokens =
        [
            "uninstall",
            "crash",
            "report",
            "launcher",
            "consultant",
            "requirementchecker",
            "setup",
            "starter",
            "remover",
            "helper",
            "beservice",
            "bink",
            "diag",
            "benchmark",
            "editor",
            "font",
            "uploader",
            "config",
            "installer",
            "updater",
            "test"
        ];

        return ignoredStemTokens.Any(token => stem.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindSteamRoot()
    {
        string[] registryPaths =
        [
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
        ];

        foreach (var registryPath in registryPaths)
        {
            var value = Registry.GetValue(registryPath, "SteamPath", null)
                        ?? Registry.GetValue(registryPath, "InstallPath", null);
            if (value is string path && Directory.Exists(path))
            {
                return path;
            }
        }

        var defaultSteamPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam");
        return Directory.Exists(defaultSteamPath) ? defaultSteamPath : null;
    }

    private static void AddIfPresent(ISet<string> roots, string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            roots.Add(path);
        }
    }

    private static ReferenceProfile BuildReferenceProfile(string installDirectory)
    {
        var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(installDirectory));
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return ReferenceProfile.Empty;
        }

        var tokens = NameTokenRegex.Matches(directoryName)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(token => token.Length >= 3 && token is not "the" and not "and" and not "for" and not "with")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var normalizedName = string.Concat(tokens);
        var acronyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (tokens.Length > 0)
        {
            var initials = string.Concat(tokens.Select(token => token[0]));
            AddAcronym(acronyms, initials);

            for (var length = 3; length <= Math.Min(4, initials.Length); length++)
            {
                AddAcronym(acronyms, initials[..length]);
            }
        }

        return new ReferenceProfile(normalizedName, tokens, acronyms);
    }

    private static void AddAcronym(ISet<string> acronyms, string value)
    {
        if (value.Length >= 3)
        {
            acronyms.Add(value);
        }
    }

    private sealed record ExecutableCandidate(string Stem, int Score, long SizeBytes);

    private sealed record ReferenceProfile(
        string NormalizedName,
        IReadOnlyList<string> SignificantTokens,
        IReadOnlySet<string> Acronyms)
    {
        public static ReferenceProfile Empty { get; } =
            new(string.Empty, Array.Empty<string>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }
}
