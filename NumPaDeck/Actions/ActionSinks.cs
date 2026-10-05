using NumPaDeck.App;
using NumPaDeck.Native;
using NumPaDeck.Presets;

namespace NumPaDeck.Actions;

/// <summary>
/// Receives the actions the router decides to run. Production uses
/// <see cref="RealActionSink"/>; the self-test injects a recording sink so no
/// real keystrokes or processes are ever started during verification.
/// </summary>
public interface IActionSink
{
    /// <summary>Run the action for a mapping (async at the sink's discretion).</summary>
    void Execute(Verb verb, string? value);

    /// <summary>
    /// Emulate "pass through" after the gesture window expires: re-send the
    /// physical numpad key via synthesized input.
    /// </summary>
    void ExecutePassthrough(string numpadKeyId);
}

/// <summary>Production sink: real SendInput injection and process launch.</summary>
public sealed class RealActionSink : IActionSink
{
    public void Execute(Verb verb, string? value)
    {
        Task.Run(() =>
        {
            try
            {
                switch (verb)
                {
                    case Verb.KeyCombo: InputInjector.SendKeyCombo(value ?? ""); break;
                    case Verb.TypeString: InputInjector.TypeText(value ?? ""); break;
                    case Verb.Media: InputInjector.PressMedia(value ?? ""); break;
                    case Verb.Launch: InputInjector.Launch(value ?? ""); break;
                    default: break; // None / Passthrough: handled elsewhere
                }
            }
            catch (Exception ex)
            {
                Log.Error("Action failed: " + ex.Message);
                Toast.Show("NumPaDeck: action failed — see errors.log");
            }
        });
    }

    public void ExecutePassthrough(string numpadKeyId)
    {
        uint vk = ResolveVk(numpadKeyId);
        if (vk == 0) return;
        NativeMethods.SendInputs(new[]
        {
            NativeMethods.KeyInput((ushort)vk, 0),
            NativeMethods.KeyInput((ushort)vk, NativeMethods.KEYEVENTF_KEYUP)
        });
    }

    private static uint ResolveVk(string id)
    {
        if (id.Length == 1 && char.IsDigit(id[0]))
            return (uint)(id[0] - '0') + NumpadKeys.VK_NUMPAD0;
        return id switch
        {
            "add" => NumpadKeys.VK_ADD,
            "sub" => NumpadKeys.VK_SUB,
            "mul" => NumpadKeys.VK_MULT,
            "div" => NumpadKeys.VK_DIV,
            "dot" => NumpadKeys.VK_DEC,
            "equal" => NumpadKeys.VK_NUMPAD_EQUAL,
            "enter" => NumpadKeys.VK_RETURN,
            _ => 0
        };
    }
}

/// <summary>Test sink: records decisions instead of performing them.</summary>
public sealed class RecordingSink : IActionSink
{
    private readonly object _gate = new();

    public List<(Verb verb, string? value)> Executed { get; } = new();
    public List<string> Passthrough { get; } = new();

    public void Execute(Verb verb, string? value)
    {
        lock (_gate)
        {
            Executed.Add((verb, value));
        }
    }

    public void ExecutePassthrough(string numpadKeyId)
    {
        lock (_gate)
        {
            Passthrough.Add(numpadKeyId);
        }
    }

    public int ExecutedCount { get { lock (_gate) return Executed.Count; } }

    public bool ExecutedEmpty
    {
        get { lock (_gate) return Executed.Count == 0; }
    }

    public bool ExecutedHas(Verb verb)
    {
        lock (_gate)
        {
            foreach (var e in Executed)
                if (e.verb == verb) return true;
            return false;
        }
    }

    public void ClearExecuted()
    {
        lock (_gate) Executed.Clear();
    }
}
