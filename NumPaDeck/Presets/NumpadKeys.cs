namespace NumPaDeck.Presets;

/// <summary>
/// Mapping between numpad virtual-key codes and the stable key ids used in
/// presets and settings. Covers the full 10-key numpad.
/// </summary>
public static class NumpadKeys
{
    public const uint VK_MULT = 0x6A;
    public const uint VK_ADD = 0x6E;
    public const uint VK_DIV = 0x6B;
    public const uint VK_SUB = 0x6D;
    public const uint VK_DEC = 0x6C;
    public const uint VK_NUMPAD0 = 0x60;
    public const uint VK_NUMPAD9 = 0x69;
    public const uint VK_NUMPAD_EQUAL = 0xBB;
    public const uint VK_RETURN = 0x0D;

    public static readonly string[] All =
    {
        "0","1","2","3","4","5","6","7","8","9",
        "add","sub","mul","div","dot","enter","equal"
    };

    /// <summary>
    /// True if the low-level-hook extended flag is set (bit 0). Numpad Enter
    /// is an extended key; the main-row Return is not.
    /// </summary>
    private static bool IsExtended(uint flags) => (flags & 0x01) != 0;

    /// <summary>
    /// Translate a VK code + hook flags into a numpad key id, or null if the
    /// key is outside our numpad scope.
    /// </summary>
    public static string? TryFromVk(uint vk, uint flags)
    {
        if (vk >= VK_NUMPAD0 && vk <= VK_NUMPAD9)
            return (vk - VK_NUMPAD0).ToString();

        switch (vk)
        {
            case VK_MULT: return "mul";
            case VK_ADD: return "add";
            case VK_DIV: return "div";
            case VK_SUB: return "sub";
            case VK_DEC: return "dot";
            case VK_NUMPAD_EQUAL: return "equal";
            case VK_RETURN:
                return IsExtended(flags) ? "enter" : null;
            default:
                return null;
        }
    }

    /// <summary>Human-friendly glyph/label for the settings numpad grid.</summary>
    public static string Display(string id) => id switch
    {
        "add" => "+",
        "sub" => "\u2212",
        "mul" => "\u00D7",
        "div" => "\u00F7",
        "dot" => ".",
        "enter" => "Enter",
        "equal" => "=",
        _ => id
    };

    /// <summary>Row-major layout for the settings numpad grid (4 columns).</summary>
    public static readonly string[] GridRows =
    {
        "7","8","9","sub",
        "4","5","6","add",
        "1","2","3","equal",
        "0","0","dot","enter"
    };
}
