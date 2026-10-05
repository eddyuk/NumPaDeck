namespace NumPaDeck.Presets;

/// <summary>
/// A named set of numpad-key -> action mappings.
/// </summary>
public class Preset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New preset";

    /// <summary>
    /// Map of numpad key id ("0".."9","add","sub","mul","div","dot","enter","equal")
    /// to the action it performs. Keys that are absent behave as Passthrough.
    /// </summary>
    public Dictionary<string, KeyMapping> Mappings { get; set; } = new();

    public Preset() { }

    public Preset(string name)
    {
        Name = name;
    }

    public Preset Clone()
    {
        var clone = new Preset
        {
            Name = Name,
            Mappings = new Dictionary<string, KeyMapping>(StringComparer.OrdinalIgnoreCase)
        };
        foreach (var kv in Mappings)
            clone.Mappings[kv.Key] = new KeyMapping(kv.Value.Verb, kv.Value.Value);
        return clone;
    }
}
