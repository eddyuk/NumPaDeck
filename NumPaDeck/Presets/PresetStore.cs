using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NumPaDeck.App;

namespace NumPaDeck.Presets;

/// <summary>
/// Loads and atomically saves the JSON settings document at
/// %APPDATA%\NumPaDeck\config.json (overridable via NUMPADECK_CONFIG, used
/// by the self-test).
/// </summary>
public static class PresetStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NumPaDeck");

    public static string ConfigPath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("NUMPADECK_CONFIG");
            return string.IsNullOrEmpty(env) ? Path.Combine(DataDir, "config.json") : env;
        }
    }

    public static AppSettings Load()
    {
        var path = ConfigPath;
        try
        {
            if (File.Exists(path))
            {
                var doc = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOpts);
                if (doc != null)
                {
                    if (doc.Gesture == null) doc.Gesture = new GestureConfig();
                    if (doc.Presets == null) doc.Presets = new List<Preset>();
                    if (doc.Presets.Count == 0) doc.Presets.Add(new Preset("Default"));
                    if (!doc.ActivePresetId.HasValue || !doc.Presets.Any(p => p.Id == doc.ActivePresetId))
                        doc.ActivePresetId = doc.Presets[0].Id;
                    return doc;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load config: " + ex.Message);
        }
        return CreateDefault();
    }

    private static AppSettings CreateDefault()
    {
        var p = new Preset("Default");
        return new AppSettings
        {
            Enabled = true,
            ActivePresetId = p.Id,
            Gesture = new GestureConfig(),
            Presets = new List<Preset> { p }
        };
    }

    /// <summary>Atomically persist the document (write to temp file, then replace).</summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            string tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
            if (File.Exists(ConfigPath))
                File.Replace(tmp, ConfigPath, null);
            else
                File.Move(tmp, ConfigPath);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save config: " + ex.Message);
        }
    }
}
