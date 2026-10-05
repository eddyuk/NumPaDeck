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
        var vks = KeyComboParser.Parse(combo);
        if (vks == null || vks.Length == 0) return;
        var inputs = new NativeMethods.INPUT[vks.Length * 2];
        for (int i = 0; i < vks.Length; i++)
            inputs[i] = NativeMethods.KeyInput((ushort)vks[i], 0);
        for (int i = 0; i < vks.Length; i++)
            inputs[vks.Length + i] = NativeMethods.KeyInput((ushort)(vks.Length - 1 - i), KeyUpFlag());
        NativeMethods.SendInputs(inputs);
    }

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
