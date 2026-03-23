namespace Buffero.Core.Configuration;

public sealed class HotkeyBinding
{
    public static readonly string[] SupportedKeys =
    [
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
    ];

    public static HotkeyBinding Default => new()
    {
        Alt = true,
        Key = "P"
    };

    public bool Ctrl { get; set; }

    public bool Alt { get; set; } = true;

    public bool Shift { get; set; }

    public string Key { get; set; } = "P";

    public void Normalize()
    {
        Key = SupportedKeys.Contains(Key, StringComparer.OrdinalIgnoreCase) ? Key.ToUpperInvariant() : "P";
    }

    public string ToDisplayString()
    {
        var parts = new List<string>(4);

        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        parts.Add(Key.ToUpperInvariant());
        return string.Join('+', parts);
    }

    public static string NormalizeExecutableToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var token = value.Trim().ToLowerInvariant();
        return token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? token[..^4]
            : token;
    }
}
