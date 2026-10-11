using NumPaDeck.Native;

namespace NumPaDeck.Actions;

/// <summary>
/// Synthesized input: key combos, typed text, and media/volume key presses.
/// All injection goes through SendInput with proper down/up pairing.
/// </summary>
public static class InputInjector
{
    public static void SendKeyCombo(string combo)
    {
        var parts = KeyComboParser.ParseParts(combo);
        if (parts == null || parts.Count == 0) return;
        var inputs = new List<NativeMethods.INPUT>(parts.Count * 2);
        foreach (var (vk, ext) in parts)
            inputs.Add(NativeMethods.KeyInput((ushort)vk, Ext(ext)));
        for (int i = parts.Count - 1; i >= 0; i--)
            inputs.Add(NativeMethods.KeyInput((ushort)parts[i].vk, KeyUpFlag() | Ext(parts[i].extended)));
        NativeMethods.SendInputs(inputs.ToArray());
    }

    private static uint Ext(bool extended) =>
        extended ? NativeMethods.KEYEVENTF_EXTENDED : 0;

    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            inputs.Add(NativeMethods.KeyInput(0, 0, unicode: c));
            inputs.Add(NativeMethods.KeyInput(0, KeyUpFlag(), unicode: c));
        }
        NativeMethods.SendInputs(inputs.ToArray());
    }

    public static void PressKey(uint vk)
    {
        NativeMethods.SendInputs(new[]
        {
            NativeMethods.KeyInput((ushort)vk, 0),
            NativeMethods.KeyInput((ushort)vk, KeyUpFlag())
        });
    }

    public static void PressMedia(string media)
    {
        uint vk = MediaAction.ToVk(media);
        if (vk != 0) PressKey(vk);
    }

    public static void Launch(string target)
    {
        string t = (target ?? "").Trim();
        if (t.Length == 0) return;

        if (Uri.TryCreate(t, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" or "mailto")
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = t,
                UseShellExecute = true
            });
            return;
        }

        // Treat as app path or shell command.
        // Simple heuristic for "command with args": split first token.
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = t,
            UseShellExecute = true
        };
        if (t.Contains(' ') && !System.IO.File.Exists(t))
        {
            int idx = t.IndexOf(' ');
            psi.FileName = t[..idx];
            psi.Arguments = t[(idx + 1)..];
        }
        System.Diagnostics.Process.Start(psi);
    }

    private static uint KeyUpFlag() => NativeMethods.KEYEVENTF_KEYUP;
}
