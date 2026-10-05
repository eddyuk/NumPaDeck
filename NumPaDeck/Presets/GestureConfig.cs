namespace NumPaDeck.Presets;

/// <summary>
/// Configuration for the numpad double-tap preset-switch gesture.
/// </summary>
public class GestureConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Numpad key id used for the gesture (default "0").</summary>
    public string Key { get; set; } = "0";

    /// <summary>
    /// Window in milliseconds within which a second tap on the gesture key
    /// counts as a double-tap (preset switch). After the window elapses the
    /// single tap fires its mapped action.
    /// </summary>
    public int TapWindowMs { get; set; } = 250;
}
