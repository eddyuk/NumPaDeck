namespace NumPaDeck.Actions;

/// <summary>
/// Maps the string form of a media action stored in presets to a virtual key
/// (VK media/volume range).
/// </summary>
public static class MediaAction
{
    public sealed class Option
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
        public uint Vk { get; init; }
    }

    public static readonly Option[] All =
    {
        new Option { Id = "playpause",  Label = "Play / Pause",  Vk = 0xB3 },
        new Option { Id = "next",       Label = "Next track",    Vk = 0xB0 },
        new Option { Id = "prev",       Label = "Prev track",    Vk = 0xB1 },
        new Option { Id = "stop",       Label = "Stop",          Vk = 0xB2 },
        new Option { Id = "volumedown", Label = "Volume down",   Vk = 0xAE },
        new Option { Id = "volumeup",   Label = "Volume up",     Vk = 0xAF },
        new Option { Id = "mute",       Label = "Mute",          Vk = 0xAD },
    };

    public static uint ToVk(string? id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        foreach (var a in All)
            if (string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))
                return a.Vk;
        return 0;
    }

    public static string? LabelOf(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var a in All)
            if (string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))
                return a.Label;
        return null;
    }
}
