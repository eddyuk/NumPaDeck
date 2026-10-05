namespace NumPaDeck.Presets;

/// <summary>
/// The action assigned to a single numpad key inside a preset.
/// </summary>
public class KeyMapping
{
    /// <summary>What the key does.</summary>
    public Verb Verb { get; set; } = Verb.None;

    /// <summary>
    /// Verb-specific payload.
    ///   KeyCombo   -> "Ctrl+Shift+T"
    ///   TypeString -> the literal text to type
    ///   Media      -> "playpause" | "next" | "voldown" | ...
    ///   Launch     -> app path, URL, or command
    ///   None/Passthrough -> null
    /// </summary>
    public string? Value { get; set; }

    public KeyMapping() { }

    public KeyMapping(Verb verb, string? value)
    {
        Verb = verb;
        Value = value;
    }
}
