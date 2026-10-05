namespace NumPaDeck.Presets;

/// <summary>
/// The action a numpad key performs when pressed.
/// </summary>
public enum Verb
{
    /// <summary>Swallow the key and do nothing.</summary>
    None = 0,

    /// <summary>Let the key pass through to the OS normally.</summary>
    Passthrough = 1,

    /// <summary>Send a key combination (e.g. Ctrl+Shift+T).</summary>
    KeyCombo = 2,

    /// <summary>Type a text string (via Unicode input).</summary>
    TypeString = 3,

    /// <summary>Fire a media / volume control.</summary>
    Media = 4,

    /// <summary>Launch an app, URL, or shell command.</summary>
    Launch = 5
}
