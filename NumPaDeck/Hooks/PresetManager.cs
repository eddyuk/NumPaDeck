using System.Text.Json;
using System.Windows.Forms;
using NumPaDeck.Actions;
using NumPaDeck.App;
using NumPaDeck.Presets;

namespace NumPaDeck.Hooks;

/// <summary>
/// The "brain": routes numpad events to actions for the active preset,
/// applies the global enable/passthrough toggle, drives the double-tap
/// preset-switch gesture, and manages presets + persistence.
/// All state transitions happen on the UI thread (low-level hook callbacks
/// are delivered there).
/// </summary>
public sealed class PresetManager
{
    private readonly object _gate = new();

    private readonly IActionSink _sink = new RealActionSink();

    public AppSettings Settings { get; private set; } = new();

    /// <summary>Raised when persisted state changed (tray/settings refresh).</summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Raised (UI thread) when the overlay settings change. Fires even inside a
    /// settings edit session, so the overlay and tray can update live while the
    /// slider is moving.
    /// </summary>
    public event EventHandler? OverlayChanged;

    /// <summary>
    /// Raised (UI thread) with a numpad key id whenever that key is pressed by
    /// the user while hijacking is active and not suspended — used purely for
    /// UI feedback (the overlay flashing the pressed cell). Subscribers must
    /// never block; exceptions are swallowed and logged.
    /// </summary>
    public event EventHandler<string>? KeyActivity;

    private void ReportKeyActivity(string keyId)
    {
        var ev = KeyActivity;
        if (ev == null) return;
        try { ev(this, keyId); }
        catch (Exception ex) { Log.Error("KeyActivity subscriber failed: " + ex.Message); }
    }

    // Physical numpad keys whose key-down we swallowed (up-pairing + repeat).
    private readonly List<string> _held = new();

    private bool HeldContains(string key)
    {
        foreach (var k in _held)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private bool HeldRemove(string key)
    {
        for (int i = 0; i < _held.Count; i++)
            if (string.Equals(_held[i], key, StringComparison.OrdinalIgnoreCase))
            {
                _held.RemoveAt(i);
                return true;
            }
        return false;
    }

    // Gesture state machine (timer-driven).
    private string _gesturePendingKey = string.Empty;
    private DateTime _gestureArmedAt;
    private readonly System.Windows.Forms.Timer _gestureTimer;

    // Edit session (settings window Save/Cancel batching).
    private int _editDepth;
    private string? _snapshotJson;

    // Suspend counter (settings dialogs open): full passthrough while > 0.
    private int _suspendCount;

    public PresetManager(IActionSink? sink = null)
    {
        if (sink != null) _sink = sink;
        bool firstRun = !File.Exists(PresetStore.ConfigPath);
        Settings = PresetStore.Load();
        if (firstRun) Persist();   // materialize %APPDATA%\NumPaDeck\config.json

        _gestureTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _gestureTimer.Tick += (s, e) =>
        {
            _gestureTimer.Stop();
            string key;
            lock (_gate)
            {
                key = _gesturePendingKey;
                _gesturePendingKey = string.Empty;
            }
            if (key.Length > 0) DispatchSingleTap(key);
        };
    }

    public Preset? ActivePreset
    {
        get
        {
            lock (_gate)
            {
                if (Settings.ActivePresetId is { } id)
                    return Settings.Presets.FirstOrDefault(p => p.Id == id);
                return Settings.Presets.FirstOrDefault();
            }
        }
    }

    internal KeyMapping MappingFor(string keyId)
    {
        lock (_gate)
        {
            var p = Settings.ActivePresetId is { } id
                ? Settings.Presets.FirstOrDefault(x => x.Id == id)
                : Settings.Presets.FirstOrDefault();
            if (p != null && p.Mappings.TryGetValue(keyId, out var m))
                return m;
            return new KeyMapping(Verb.Passthrough, null);
        }
    }

    // ------------------------------------------------------------------
    // Hook entry point (UI thread, must stay fast)
    // Returns true when the event should be swallowed (blocked from the OS).
    // ------------------------------------------------------------------
    public bool OnNumpadKey(string keyId, bool isDown)
    {
        if (_suspendCount > 0) return false;      // settings UI open: passthrough

        bool enabled;
        lock (_gate) enabled = Settings.Enabled;
        if (!enabled) return false;               // global passthrough toggle

        if (!isDown)
        {
            // key-up: swallow only when we swallowed the matching down
            // (prevents stuck keys; releasing does NOT cancel an armed tap —
            // a real double-tap has a key-up between the two key-downs).
            bool held;
            lock (_gate) held = HeldRemove(keyId);
            return held;
        }

        // --- key-down ---
        bool repeat;
        lock (_gate)
        {
            repeat = HeldContains(keyId);
            _held.Add(keyId);
        }

        bool gestureEnabled;
        string gestureKey = string.Empty;
        int windowMs = 250;
        lock (_gate)
        {
            gestureEnabled = Settings.Gesture.Enabled;
            gestureKey = Settings.Gesture.Key ?? string.Empty;
            windowMs = Settings.Gesture.TapWindowMs;
        }
        bool isGestureKey = gestureEnabled
            && !string.IsNullOrEmpty(gestureKey)
            && string.Equals(keyId, gestureKey, StringComparison.OrdinalIgnoreCase);

        if (repeat)
        {
            // Holding the key down: never a double-tap, and only Media may re-fire.
            ReportKeyActivity(keyId);
            if (isGestureKey) return true;
            var rep = MappingFor(keyId);
            if (rep.Verb == Verb.Media) _sink.Execute(rep.Verb, rep.Value);
            return true; // swallow further repeats
        }

        if (isGestureKey)
        {
            bool doubleTap;
            lock (_gate)
            {
                doubleTap = _gesturePendingKey.Length > 0
                    && string.Equals(keyId, _gesturePendingKey, StringComparison.OrdinalIgnoreCase)
                    && (DateTime.Now - _gestureArmedAt).TotalMilliseconds <= windowMs;

                if (doubleTap)
                {
                    _gesturePendingKey = string.Empty;
                    _gestureTimer.Stop();
                }
                else
                {
                    _gesturePendingKey = keyId;
                    _gestureArmedAt = DateTime.Now;
                    _gestureTimer.Interval = Math.Max(50, windowMs);
                }
            }

            if (doubleTap)
            {
                CycleActivePreset();
            }
            else
            {
                _gestureTimer.Start();
            }
            ReportKeyActivity(keyId);
            return true; // swallowed either way; key-up via _held pairing
        }

        // Any other key-down resolves an armed gesture tap immediately.
        string pending = string.Empty;
        lock (_gate)
        {
            pending = _gesturePendingKey;
            if (pending.Length > 0)
            {
                _gesturePendingKey = string.Empty;
                _gestureTimer.Stop();
            }
        }
        if (pending.Length > 0) DispatchSingleTap(pending);

        var mapping = MappingFor(keyId);
        ReportKeyActivity(keyId);
        if (mapping.Verb == Verb.Passthrough)
        {
            lock (_gate) HeldRemove(keyId); // let the physical key-up through
            return false;
        }

        if (mapping.Verb != Verb.None)
            _sink.Execute(mapping.Verb, mapping.Value);
        return true; // "none" and real actions are both swallowed
    }

    /// <summary>
    /// Resolve an armed single tap by applying the key's mapping. Passthrough
    /// is emulated by re-sending the physical key through the action sink.
    /// </summary>
    internal void DispatchSingleTap(string keyId)
    {
        var m = MappingFor(keyId);
        ReportKeyActivity(keyId);
        switch (m.Verb)
        {
            case Verb.None:
                break; // swallow: single tap of "none" does nothing
            case Verb.Passthrough:
                _sink.ExecutePassthrough(keyId);
                break;
            default:
                _sink.Execute(m.Verb, m.Value);
                break;
        }
    }

    // ------------------------------------------------------------------
    // Suspend / edit sessions
    // ------------------------------------------------------------------

    /// <summary>Ref-counted: while > 0, every numpad key passes through.</summary>
    public void Suspend()
    {
        if (++_suspendCount == 1)
        {
            lock (_gate) _gesturePendingKey = string.Empty;
            _gestureTimer.Stop();
        }
    }

    public void Resume()
    {
        if (_suspendCount > 0) _suspendCount--;
    }

    /// <summary>
    /// Batch persistence while the settings window is open: mutations are
    /// still applied to live state (so the gesture/hook always see current
    /// mappings), but the file is not written and the tray is not refreshed.
    /// </summary>
    public void BeginEdit()
    {
        if (_editDepth == 0)
            _snapshotJson = JsonSerializer.Serialize(Settings);
        _editDepth++;
    }

    public void EndEdit(bool commit)
    {
        if (_editDepth == 0) return;
        _editDepth--;
        if (_editDepth > 0) return;

        string? snap = _snapshotJson;
        _snapshotJson = null;

        if (!commit && snap != null)
        {
            try
            {
                var restored = JsonSerializer.Deserialize<AppSettings>(snap);
                if (restored != null) Settings = restored;
            }
            catch (Exception ex) { Log.Error("Restore failed: " + ex.Message); }
        }

        Persist();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AfterModify()
    {
        if (_editDepth > 0) return;
        Persist();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------
    // Preset / settings management
    // ------------------------------------------------------------------

    public void CycleActivePreset()
    {
        List<Preset> list;
        Guid? current;
        lock (_gate)
        {
            list = new List<Preset>(Settings.Presets);
            current = Settings.ActivePresetId;
        }
        if (list.Count == 0) return;

        Guid nextId;
        if (list.Count == 1)
        {
            nextId = list[0].Id;
        }
        else
        {
            int idx = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Id == current) { idx = i; break; }
            nextId = list[(idx + 1) % list.Count].Id;
        }

        lock (_gate) Settings.ActivePresetId = nextId;
        Persist();
        Toast.Show("Preset: " + (ActivePreset?.Name ?? "?"));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Preset? GetPreset(Guid id)
    {
        lock (_gate) return Settings.Presets.FirstOrDefault(p => p.Id == id);
    }

    public Preset CreatePreset(string name)
    {
        var p = new Preset(string.IsNullOrWhiteSpace(name) ? "Preset" : name.Trim());
        lock (_gate)
        {
            Settings.Presets.Add(p);
            if (!Settings.ActivePresetId.HasValue) Settings.ActivePresetId = p.Id;
        }
        AfterModify();
        return p;
    }

    public Preset? DuplicatePreset(Guid id)
    {
        Preset? clone;
        lock (_gate)
        {
            var src = Settings.Presets.FirstOrDefault(p => p.Id == id);
            if (src == null) return null;
            clone = src.Clone();
            clone.Name = src.Name + " copy";
            int at = Settings.Presets.FindIndex(p => p.Id == id);
            Settings.Presets.Insert(at < 0 ? Settings.Presets.Count : at + 1, clone);
        }
        AfterModify();
        return clone;
    }

    public bool DeletePreset(Guid id)
    {
        bool changed = false;
        lock (_gate)
        {
            if (Settings.Presets.Count <= 1) return false; // never delete the last one
            var p = Settings.Presets.FirstOrDefault(x => x.Id == id);
            if (p == null) return false;
            Settings.Presets.Remove(p);
            if (Settings.ActivePresetId == id)
                Settings.ActivePresetId = Settings.Presets[0].Id;
            changed = true;
        }
        if (changed) AfterModify();
        return changed;
    }

    public void RenamePreset(Guid id, string name)
    {
        lock (_gate)
        {
            var p = Settings.Presets.FirstOrDefault(x => x.Id == id);
            if (p != null) p.Name = string.IsNullOrWhiteSpace(name) ? "Preset" : name.Trim();
        }
        AfterModify();
    }

    public void SetActivePreset(Guid id)
    {
        lock (_gate) Settings.ActivePresetId = id;
        AfterModify();
    }

    public void SetEnabled(bool on)
    {
        lock (_gate) Settings.Enabled = on;
        AfterModify();
    }

    public KeyMapping? GetMapping(Guid presetId, string keyId)
    {
        lock (_gate)
        {
            var p = Settings.Presets.FirstOrDefault(x => x.Id == presetId);
            if (p == null) return null;
            return p.Mappings.TryGetValue(keyId, out var m) ? m : null;
        }
    }

    public void SetMapping(Guid presetId, string keyId, KeyMapping? mapping)
    {
        lock (_gate)
        {
            var p = Settings.Presets.FirstOrDefault(x => x.Id == presetId);
            if (p == null) return;
            if (mapping == null || mapping.Verb == Verb.Passthrough)
                p.Mappings.Remove(keyId);   // absence in the map = passthrough
            else
                p.Mappings[keyId] = new KeyMapping(mapping.Verb, mapping.Value);
        }
        AfterModify();
    }

    public void SetGesture(bool enabled, string key, int windowMs)
    {
        lock (_gate)
        {
            Settings.Gesture.Enabled = enabled;
            Settings.Gesture.Key = string.IsNullOrWhiteSpace(key) ? "0" : key;
            Settings.Gesture.TapWindowMs = Math.Clamp(windowMs, 80, 2000);
        }
        AfterModify();
    }

    public void SetOverlay(bool visible, double opacity)
    {
        opacity = Math.Clamp(opacity, 0.15, 1.0);
        lock (_gate)
        {
            Settings.Overlay.Visible = visible;
            Settings.Overlay.Opacity = opacity;
        }
        AfterModify();
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetOverlayPosition(int x, int y)
    {
        lock (_gate)
        {
            Settings.Overlay.X = x;
            Settings.Overlay.Y = y;
        }
        AfterModify();
    }

    public void Persist()
    {
        try { PresetStore.Save(Settings); }
        catch (Exception ex) { Log.Error("Persist failed: " + ex.Message); }
    }
}
