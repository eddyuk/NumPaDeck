using System.Text.RegularExpressions;

namespace NumPaDeck.Actions;

/// <summary>
/// Parses a human combo string like "Ctrl+Shift+T" into an ordered array of
/// virtual keys (modifiers first in canonical order, then the primary key).
/// </summary>
public static class KeyComboParser
{
    private static readonly Dictionary<string, uint> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = 0x0D, ["return"] = 0x0D,
        ["space"] = 0x20,
        ["tab"] = 0x09,
        ["esc"] = 0x1B, ["escape"] = 0x1B,
        ["backspace"] = 0x08,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["home"] = 0x24, ["end"] = 0x23,
        ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["insert"] = 0x2D,
        ["del"] = 0x2E, ["delete"] = 0x2E,
        ["printscreen"] = 0x2C,
        ["capslock"] = 0x14,
        ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73,
        ["f5"] = 0x74, ["f6"] = 0x75, ["f7"] = 0x76, ["f8"] = 0x77,
        ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B,
        ["numpad0"] = 0x60, ["numpad1"] = 0x61, ["numpad2"] = 0x62,
        ["numpad3"] = 0x63, ["numpad4"] = 0x64, ["numpad5"] = 0x65,
        ["numpad6"] = 0x66, ["numpad7"] = 0x67, ["numpad8"] = 0x68,
        ["numpad9"] = 0x69,
        ["numpadadd"] = 0x6E, ["numpadsub"] = 0x6D, ["numpadmul"] = 0x6A,
        ["numpaddiv"] = 0x6B, ["numpaddot"] = 0x6C,
        ["numpadenter"] = 0x0D, ["numpadequal"] = 0xBB,
        // synonyms
        ["numpadmultiply"] = 0x6A, ["numpaddivide"] = 0x6B,
        ["numpaddecimal"] = 0x6C, ["numpadequals"] = 0xBB
    };

    /// <summary>
    /// Tokens that must be sent with KEYEVENTF_EXTENDEDKEY (the numpad is an
    /// extended-key set; without the flag NumpadEnter would arrive as the
    /// main-row Enter and Numpad0 as a scan-code mismatch).
    /// </summary>
    private static readonly HashSet<string> ExtendedAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "numpad0", "numpad1", "numpad2", "numpad3", "numpad4",
        "numpad5", "numpad6", "numpad7", "numpad8", "numpad9",
        "numpadadd", "numpadsub",
        "numpadmul", "numpadmultiply",
        "numpaddiv", "numpaddivide",
        "numpaddot", "numpaddecimal",
        "numpadenter", "numpadequal", "numpadequals"
    };

    private static readonly Dictionary<string, uint> ModifierAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0x11, ["control"] = 0x11,
        ["alt"] = 0x12,
        ["shift"] = 0x10,
        ["win"] = 0x5B, ["windows"] = 0x5B, ["cmd"] = 0x5B, ["meta"] = 0x5B, ["super"] = 0x5B
    };

    // Canonical emit order: Ctrl, Alt, Shift, Win
    private static readonly uint[] CanonicalModifierOrder = { 0x11, 0x12, 0x10, 0x5B };

    /// <summary>
    /// Returns the ordered parts of a valid combo — modifiers (never extended)
    /// followed by the primary key, each tagged with whether it must be sent
    /// as an extended key. Null when the combo is invalid.
    /// </summary>
    public static List<(uint vk, bool extended)>? ParseParts(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return null;

        string[] parts = Regex.Split(combo, @"\+|,")
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
        if (parts.Length == 0) return null;

        var mods = new List<uint>();
        string? primaryToken = null;

        foreach (string token in parts)
        {
            if (ModifierAliases.TryGetValue(token, out uint mv))
            {
                if (!mods.Contains(mv)) mods.Add(mv);
                continue;
            }

            uint vk = ResolveKey(token);
            if (vk == 0) return null;        // unknown key
            if (primaryToken != null) return null; // more than one non-modifier key
            primaryToken = token;
        }

        if (primaryToken == null) return null; // modifiers alone (e.g. just "Ctrl")

        var result = new List<(uint vk, bool extended)>(2);
        foreach (uint m in CanonicalModifierOrder)
            if (mods.Contains(m)) result.Add((m, false));
        result.Add((ResolveKey(primaryToken), ExtendedAliases.Contains(primaryToken)));
        return result;
    }

    /// <summary>Returns the ordered VK codes, or null if the combo is invalid.</summary>
    public static uint[]? Parse(string combo)
    {
        var parts = ParseParts(combo);
        return parts == null ? null : parts.Select(p => p.vk).ToArray();
    }

    private static uint ResolveKey(string token)
    {
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z') return (uint)(int)c;
            if (c is >= '0' and <= '9') return (uint)(int)c;
        }
        if (KeyAliases.TryGetValue(token, out uint vk)) return vk;
        return 0;
    }
}
