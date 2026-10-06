using NumPaDeck.Actions;
using NumPaDeck.Hooks;
using NumPaDeck.Native;
using NumPaDeck.Presets;

namespace NumPaDeck.App;

/// <summary>
/// Offline self-test: validates the combo parser, numpad VK mapping, media
/// key table, settings persistence, and routing decisions — using a
/// recording action sink so NO real keystrokes or processes are started.
///
///   dotnet run --project NumPaDeck -- --selftest
/// </summary>
public static class SelfTest
{
    private static int _passed;
    private static int _failed;

    public static int Run()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "npd_selftest_" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable("NUMPADECK_CONFIG", tmp);
        try
        {
            Console.WriteLine("NumPaDeck self-test");
            Console.WriteLine("===================");
            CheckComboParser();
            CheckNumpadKeyMapping();
            CheckMedia();
            CheckInjectionLayout();
            CheckStoreRoundtrip();
            CheckRouting();
            Console.WriteLine("===================");
            Console.WriteLine($"Result: {_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUMPADECK_CONFIG", null);
            try { File.Delete(tmp); } catch { }
        }
    }

    private static void Check(string name, bool ok)
    {
        if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
        else { _failed++; Console.WriteLine("  FAIL  " + name); }
    }

    private static void CheckComboParser()
    {
        Console.WriteLine("Combo parser:");
        var c = KeyComboParser.Parse("Ctrl+Shift+T");
        Check("Ctrl+Shift+T -> Ctrl Shift T",
            c is { Length: 3 } && c[0] == 0x11 && c[1] == 0x10 && c[2] == 0x54);

        var a = KeyComboParser.Parse("alt+f4");
        Check("alt+f4 -> Alt F4",
            a is { Length: 2 } && a[0] == 0x12 && a[1] == 0x73);

        var w = KeyComboParser.Parse("Cmd+Space");
        Check("Cmd+Space -> Win Space",
            w is { Length: 2 } && w[0] == 0x5B && w[1] == 0x20);

        var d = KeyComboParser.Parse("Ctrl+5");
        Check("Ctrl+5 -> Ctrl '5'",
            d is { Length: 2 } && d[0] == 0x11 && d[1] == 0x35);

        Check("bare modifier rejected", KeyComboParser.Parse("Ctrl") == null);
        Check("empty input rejected", KeyComboParser.Parse("   ") == null);
        Check("two primary keys rejected", KeyComboParser.Parse("Ctrl+A+B") == null);
    }

    private static void CheckNumpadKeyMapping()
    {
        Console.WriteLine("Numpad VK mapping:");
        Check("VK 0x60 -> '0'", NumpadKeys.TryFromVk(0x60, 0) == "0");
        Check("VK 0x69 -> '9'", NumpadKeys.TryFromVk(0x69, 0) == "9");
        Check("VK_MULT -> mul", NumpadKeys.TryFromVk(0x6A, 0) == "mul");
        Check("VK_ADD -> add", NumpadKeys.TryFromVk(0x6E, 0) == "add");
        Check("VK_SUB -> sub", NumpadKeys.TryFromVk(0x6D, 0) == "sub");
        Check("VK_DIV -> div", NumpadKeys.TryFromVk(0x6B, 0) == "div");
        Check("VK_DEC -> dot", NumpadKeys.TryFromVk(0x6C, 0) == "dot");
        Check("VK num-equal -> equal", NumpadKeys.TryFromVk(0xBB, 0) == "equal");
        Check("Numpad Enter (extended) -> enter", NumpadKeys.TryFromVk(0x0D, 0x01) == "enter");
        Check("Main Enter (not extended) ignored", NumpadKeys.TryFromVk(0x0D, 0x00) == null);
        Check("VK_A ignored", NumpadKeys.TryFromVk(0x41, 0) == null);
        Check("All 17 key ids present", NumpadKeys.All.Length == 17);
    }

    private static void CheckMedia()
    {
        Console.WriteLine("Media keys:");
        Check("playpause -> 0xB3", MediaAction.ToVk("playpause") == 0xB3);
        Check("next -> 0xB0", MediaAction.ToVk("next") == 0xB0);
        Check("prev -> 0xB1", MediaAction.ToVk("prev") == 0xB1);
        Check("stop -> 0xB2", MediaAction.ToVk("stop") == 0xB2);
        Check("volumeup -> 0xAF", MediaAction.ToVk("volumeup") == 0xAF);
        Check("mute -> 0xAD", MediaAction.ToVk("mute") == 0xAD);
        Check("unknown -> 0", MediaAction.ToVk("warp-drive") == 0);
        Check("null -> 0", MediaAction.ToVk(null) == 0);
    }

    private static void CheckInjectionLayout()
    {
        Console.WriteLine("SendInput layout:");
        // Win32 rule: with KEYEVENTF_UNICODE the character must be in wScan and wVk is ignored.
        var u = NativeMethods.KeyInput(0, 0, unicode: 'A');
        Check("unicode char lands in wScan", u.U.ki.wScan == (ushort)'A');
        Check("unicode flag set", (u.U.ki.dwFlags & NativeMethods.KEYEVENTF_UNICODE) != 0);
        Check("unicode vk stays 0", u.U.ki.wVk == 0);

        var up = NativeMethods.KeyInput(0, NativeMethods.KEYEVENTF_KEYUP, unicode: '\u010F');
        Check("unicode keyup keeps char + both flags",
            up.U.ki.wScan == (ushort)'\u010F'
            && (up.U.ki.dwFlags & (NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP))
                == (NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP));

        var vk = NativeMethods.KeyInput(0x41, 0);
        Check("plain vk unchanged, no unicode flag",
            vk.U.ki.wVk == 0x41 && (vk.U.ki.dwFlags & NativeMethods.KEYEVENTF_UNICODE) == 0);
    }

    private static void CheckStoreRoundtrip()
    {
        Console.WriteLine("Settings store round-trip:");
        var doc = new AppSettings
        {
            Enabled = true,
            Gesture = new GestureConfig { Enabled = true, Key = "5", TapWindowMs = 320 },
            Overlay = new OverlayConfig { Visible = true, Opacity = 0.6, X = 200, Y = 120 }
        };
        var a = new Preset("Deck A");
        a.Mappings["7"] = new KeyMapping(Verb.Media, "next");
        a.Mappings["0"] = new KeyMapping(Verb.None, null);
        var b = new Preset("Deck B");
        b.Mappings["1"] = new KeyMapping(Verb.KeyCombo, "Ctrl+Shift+T");
        doc.Presets = new List<Preset> { a, b };
        doc.ActivePresetId = b.Id;

        PresetStore.Save(doc);
        var back = PresetStore.Load();

        Check("2 presets survive", back.Presets.Count == 2);
        Check("active id survives", back.ActivePresetId == b.Id);
        Check("media mapping survives",
            back.Presets[0].Mappings.TryGetValue("7", out var m7)
            && m7.Verb == Verb.Media && m7.Value == "next");
        Check("combo mapping survives",
            back.Presets[1].Mappings.TryGetValue("1", out var m1) && m1.Verb == Verb.KeyCombo);
        Check("gesture config survives",
            back.Gesture.Enabled && back.Gesture.Key == "5" && back.Gesture.TapWindowMs == 320);
        Check("enabled flag survives", back.Enabled);
        Check("overlay visible + opacity survive",
            back.Overlay != null && back.Overlay.Visible
            && Math.Abs(back.Overlay.Opacity - 0.6) < 0.001);
        Check("overlay position survives",
            back.Overlay != null && back.Overlay.X == 200 && back.Overlay.Y == 120);
    }

    private static void CheckRouting()
    {
        Console.WriteLine("Routing (recording sink — no real input):");
        var sink = new RecordingSink();
        var m = new PresetManager(sink);

        var deckA = m.GetPreset(m.Settings.Presets[0].Id)!; // "0"=None, "7"=Media
        var deckB = m.GetPreset(m.Settings.Presets[1].Id)!; // "1"=KeyCombo

        m.SetActivePreset(deckA.Id);

        Check("'none' swallows down", m.OnNumpadKey("0", true));
        Check("'none' swallows up (paired)", m.OnNumpadKey("0", false));
        Check("'none' executes nothing", sink.ExecutedEmpty);

        Check("unmapped key passes down", !m.OnNumpadKey("4", true));
        Check("unmapped key passes up", !m.OnNumpadKey("4", false));

        m.SetEnabled(false);
        Check("disabled = full passthrough", !m.OnNumpadKey("0", true) && !m.OnNumpadKey("0", false));
        m.SetEnabled(true);

        m.SetActivePreset(deckB.Id);

        Check("combo swallows both edges", m.OnNumpadKey("1", true) && m.OnNumpadKey("1", false));
        Check("combo fires exactly once", sink.ExecutedCount == 1 && sink.ExecutedHas(Verb.KeyCombo));

        sink.Executed.Clear();
        m.OnNumpadKey("1", true);   // physical down
        m.OnNumpadKey("1", true);   // autorepeat while held
        m.OnNumpadKey("1", false);  // release
        Check("autorepeat does not re-fire", sink.ExecutedCount == 1);

        m.SetGesture(true, "8", 400);
        Guid before = m.ActivePreset!.Id;
        bool t1 = m.OnNumpadKey("8", true);
        bool r1 = m.OnNumpadKey("8", false);
        bool t2 = m.OnNumpadKey("8", true);
        bool r2 = m.OnNumpadKey("8", false);
        Check("double-tap: all events swallowed", t1 && r1 && t2 && r2);
        Check("double-tap switched preset", m.ActivePreset?.Id != before);

        Guid held = m.ActivePreset!.Id;
        m.OnNumpadKey("8", true);
        m.OnNumpadKey("8", true); // repeat while held — must NOT count as second tap
        m.OnNumpadKey("8", false);
        Check("holding down does not cycle", m.ActivePreset?.Id == held);
        sink.Executed.Clear();

        // Single-tap resolution (what the gesture timer triggers after the window).
        m.SetActivePreset(deckA.Id);
        m.DispatchSingleTap("2"); // unmapped -> emulated physical passthrough
        Check("single tap of unmapped key = passthrough", sink.Passthrough.Contains("2"));

        sink.Executed.Clear();
        sink.Passthrough.Clear();
        m.DispatchSingleTap("0"); // mapped "none" -> swallowed
        Check("single tap of 'none' key does nothing", sink.ExecutedEmpty && sink.Passthrough.Count == 0);

        m.DispatchSingleTap("7"); // mapped Media "next"
        Check("single tap of mapped key fires action", sink.ExecutedHas(Verb.Media));

        // Suspend (settings UI open) forces passthrough even for mapped keys.
        sink.Executed.Clear();
        m.Suspend();
        Check("suspended = passthrough", !m.OnNumpadKey("7", true) && !m.OnNumpadKey("7", false));
        m.Resume();
        Check("resumed = mapped again", m.OnNumpadKey("7", true) && m.OnNumpadKey("7", false));

        // Overlay settings: clamped apply.
        m.SetOverlay(false, 2.5);
        Check("SetOverlay clamps opacity high",
            Math.Abs(m.Settings.Overlay.Opacity - 1.0) < 0.001 && !m.Settings.Overlay.Visible);
        m.SetOverlay(true, 0.02);
        Check("SetOverlay clamps opacity low",
            Math.Abs(m.Settings.Overlay.Opacity - 0.15) < 0.001 && m.Settings.Overlay.Visible);

        // Key-activity feed (drives the overlay's pressed-key flash).
        string? activity = null;
        m.KeyActivity += (s, k) => activity = k;
        m.OnNumpadKey("4", true);
        Check("KeyActivity raised on key press", activity == "4");
        m.OnNumpadKey("4", false);
    }
}
