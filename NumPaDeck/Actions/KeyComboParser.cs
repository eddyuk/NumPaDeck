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
        ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B
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

    /// <summary>Returns the ordered VK codes, or null if the combo is invalid.</summary>
    public static uint[]? Parse(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return null;

        string[] parts = Regex.Split(combo, @"\+|,")
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
        if (parts.Length == 0) return null;

        var mods = new List<uint>();
        uint primary = 0;
        bool havePrimary = false;

        foreach (string token in parts)
        {
            if (ModifierAliases.TryGetValue(token, out uint mv))
            {
                if (!mods.Contains(mv)) mods.Add(mv);
                continue;
            }

            uint vk = ResolveKey(token);
            if (vk == 0) return null;        // unknown key
            if (havePrimary) return null;    // more than one non-modifier key
            primary = vk;
            havePrimary = true;
        }

        if (!havePrimary) return null;       // modifiers alone (e.g. just "Ctrl")

        var result = new List<uint>();
        foreach (uint m in CanonicalModifierOrder)
            if (mods.Contains(m)) result.Add(m);
        result.Add(primary);
        return result.ToArray();
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
