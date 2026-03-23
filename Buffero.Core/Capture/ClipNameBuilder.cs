namespace Buffero.Core.Capture;

public static class ClipNameBuilder
{
    public static string Build(string pattern, DateTimeOffset timestamp, string? gameName)
    {
        var safePattern = string.IsNullOrWhiteSpace(pattern)
            ? "Buffero-{timestamp}-{game}"
            : pattern.Trim();

        var safeGame = SanitizeToken(string.IsNullOrWhiteSpace(gameName) ? "manual" : gameName);
        var resolved = safePattern
            .Replace("{timestamp}", timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmss"))
            .Replace("{date}", timestamp.ToLocalTime().ToString("yyyyMMdd"))
            .Replace("{time}", timestamp.ToLocalTime().ToString("HHmmss"))
            .Replace("{game}", safeGame, StringComparison.OrdinalIgnoreCase);

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(resolved.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray())
            .Trim()
            .Trim('.');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = $"Buffero-{timestamp.ToLocalTime():yyyyMMdd-HHmmss}";
        }

        return sanitized.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            ? sanitized
            : $"{sanitized}.mp4";
    }

    private static string SanitizeToken(string input)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(input
            .Where(ch => !invalidChars.Contains(ch))
            .Select(ch => char.IsWhiteSpace(ch) ? '-' : ch)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized)
            ? "manual"
            : sanitized;
    }
}
