namespace NumPaDeck.Presets;

/// <summary>
/// Root persisted document for NumPaDeck. Written to
/// %APPDATA%\NumPaDeck\config.json
/// </summary>
public class AppSettings
{
    public bool Enabled { get; set; } = true;

    public Guid? ActivePresetId { get; set; }

    public GestureConfig Gesture { get; set; } = new();

    public List<Preset> Presets { get; set; } = new();
}
