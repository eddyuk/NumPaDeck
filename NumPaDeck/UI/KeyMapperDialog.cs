using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using NumPaDeck.Actions;
using NumPaDeck.App;
using NumPaDeck.Hooks;
using NumPaDeck.Presets;

namespace NumPaDeck.UI;

/// <summary>
/// Dialog to assign a verb + value to a single numpad key in one preset.
/// Supports live combo capture and a Test button that performs the action
/// immediately so the mapping can be sanity-checked.
/// </summary>
public sealed class KeyMapperDialog : Form
{
    private static readonly Color Faint = Color.FromArgb(125, 130, 135);

    private readonly PresetManager _manager;
    private readonly Preset _preset;
    private readonly string _keyId;

    private readonly ComboBox _verbCombo;
    private readonly Label _valueLabel;
    private readonly TextBox _comboBox;
    private readonly Button _captureButton;
    private readonly Label _captureHint;
    private readonly Label _launchHint;
    private readonly TextBox _stringBox;
    private readonly ComboBox _mediaCombo;
    private readonly TextBox _launchBox;
    private readonly Button _testButton;

    private bool _capturing;
    private bool _suppressRebuild = true; // until the constructor has built the value panel
    private static System.Windows.Forms.Timer? _captureTimeout;

    private sealed record VerbOption(Verb Verb, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class MediaOption
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
        public override string ToString() => Label;
    }

    public KeyMapperDialog(PresetManager manager, Preset preset, string keyId)
    {
        _manager = manager;
        _preset = preset;
        _keyId = keyId;

        Text = "Map Numpad " + NumpadKeys.Display(keyId) + "  —  preset " + preset.Name;
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(470, 326);

        Controls.Add(new Label
        {
            Text = "ACTION",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Faint,
            AutoSize = true,
            Location = new Point(20, 14)
        });

        _verbCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(20, 38),
            Size = new Size(430, 26),
            Font = new Font("Segoe UI", 10f)
        };
        _verbCombo.Items.AddRange(new VerbOption[]
        {
            new(Verb.None, "Do nothing (swallow the keypress)"),
            new(Verb.Passthrough, "Pass through (keep normal behavior)"),
            new(Verb.KeyCombo, "Send a key combo (e.g. Ctrl+Shift+T)"),
            new(Verb.TypeString, "Type a string of text"),
            new(Verb.Media, "Media / volume control"),
            new(Verb.Launch, "Launch app, URL, or command"),
        });
        _verbCombo.SelectedIndexChanged += (s, e) =>
        {
            if (!_suppressRebuild) RebuildValuePanel();
        };

        // Select the currently stored verb (absence = Passthrough).
        Verb currentVerb = _preset.Mappings.TryGetValue(_keyId, out var cur) ? cur.Verb : Verb.Passthrough;
        int defaultIdx = 0;
        for (int i = 0; i < _verbCombo.Items.Count; i++)
        {
            if (_verbCombo.Items[i] is VerbOption vo && vo.Verb == currentVerb) { defaultIdx = i; break; }
        }
        _verbCombo.SelectedIndex = defaultIdx;

        _valueLabel = new Label
        {
            AutoSize = true,
            Location = new Point(20, 78),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Faint
        };

        var valuePanel = new Panel { Location = new Point(20, 100), Size = new Size(430, 132) };

        _comboBox = new TextBox { Location = new Point(0, 24), Size = new Size(278, 26), Font = new Font("Segoe UI", 10f) };
        _captureButton = new Button { Text = "Capture combo…", Location = new Point(286, 23), Size = new Size(144, 28) };
        _captureButton.Click += (s, e) => ToggleCapture();
        _captureHint = new Label
        {
            Text = "Click “Capture combo…”, then press the combo on your keyboard. Esc cancels capture.",
            Location = new Point(0, 58),
            Size = new Size(430, 20),
            Font = new Font("Segoe UI", 8f),
            ForeColor = Faint
        };

        _stringBox = new TextBox
        {
            Location = new Point(0, 24),
            Size = new Size(430, 64),
            Multiline = true,
            Font = new Font("Segoe UI", 10f)
        };

        _mediaCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(0, 24),
            Size = new Size(220, 26),
            Font = new Font("Segoe UI", 10f)
        };
        foreach (var m in MediaAction.All)
            _mediaCombo.Items.Add(new MediaOption { Id = m.Id, Label = m.Label });
        _mediaCombo.SelectedIndex = 0;

        _launchBox = new TextBox { Location = new Point(0, 24), Size = new Size(430, 26), Font = new Font("Segoe UI", 10f) };
        _launchHint = new Label
        {
            Text = "Examples:  notepad.exe   ·   https://example.com   ·   \"C:\\Tools\\tool.exe --flag\"",
            Location = new Point(0, 58),
            Size = new Size(430, 20),
            Font = new Font("Segoe UI", 8f),
            ForeColor = Faint
        };

        _testButton = new Button { Text = "Test", Location = new Point(0, 84), Size = new Size(90, 30) };
        _testButton.Click += (s, e) => RunTest();

        valuePanel.Controls.AddRange(new Control[]
        {
            _comboBox, _captureButton, _captureHint,
            _stringBox, _mediaCombo, _launchBox, _launchHint, _testButton
        });

        // Preset status banner — makes it unmistakable whether edits apply now.
        bool isActive = _manager.ActivePreset?.Id == _preset.Id;
        var statusPanel = new Panel
        {
            Location = new Point(20, 240),
            Size = new Size(430, 26),
            BackColor = isActive ? Color.FromArgb(232, 245, 233) : Color.FromArgb(253, 243, 224)
        };
        statusPanel.Controls.Add(new Label
        {
            Text = isActive
                ? "● Active preset — mappings apply immediately."
                : "● NOT ACTIVE — activate this preset (double-click it in the left list) for mappings to apply.",
            Location = new Point(8, 4),
            Size = new Size(414, 20),
            Font = new Font("Segoe UI", 8.5f, isActive ? FontStyle.Regular : FontStyle.Bold),
            ForeColor = isActive ? Color.FromArgb(27, 94, 32) : Color.FromArgb(140, 92, 0)
        });

        var ok = new Button { Text = "OK", Location = new Point(274, 276), Size = new Size(84, 34) };
        ok.Click += (s, e) => OnOk();
        var cancel = new Button { Text = "Cancel", Location = new Point(366, 276), Size = new Size(84, 34) };
        cancel.Click += (s, e) => Close();

        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(_verbCombo);
        Controls.Add(_valueLabel);
        Controls.Add(valuePanel);
        Controls.Add(statusPanel);
        Controls.Add(ok);
        Controls.Add(cancel);

        Shown += (s, e) => _manager.Suspend();   // numpad must reach this dialog while open
        FormClosed += (s, e) => _manager.Resume();

        RebuildValuePanel(firstTime: true);
        _suppressRebuild = false; // value panel is fully built — user edits may drive it now
    }

    // ------------------------------------------------------------------
    // Verb -> value panel
    // ------------------------------------------------------------------

    private void RebuildValuePanel(bool firstTime = false)
    {
        if (_verbCombo.SelectedItem is not VerbOption opt) return;
        string? curValue = _preset.Mappings.TryGetValue(_keyId, out var mm) ? mm.Value : null;

        bool combo = opt.Verb == Verb.KeyCombo;
        bool typeString = opt.Verb == Verb.TypeString;
        bool media = opt.Verb == Verb.Media;
        bool launch = opt.Verb == Verb.Launch;

        _comboBox.Visible = combo;
        _captureButton.Visible = combo;
        _captureHint.Visible = combo;
        _stringBox.Visible = typeString;
        _mediaCombo.Visible = media;
        _launchBox.Visible = launch;
        _launchHint.Visible = launch;

        bool hasTest = combo || media || launch;
        _testButton.Visible = hasTest;
        // Position below the tallest input/hint above it so it never overlaps.
        _testButton.Location = new Point(0, media ? 56 : 84);

        _valueLabel.Text = opt.Verb switch
        {
            Verb.None => "This key will be swallowed and do nothing.",
            Verb.Passthrough => "This key behaves normally — no action assigned.",
            Verb.KeyCombo => "COMBO TO SEND",
            Verb.TypeString => "TEXT TO TYPE",
            Verb.Media => "MEDIA ACTION",
            Verb.Launch => "TARGET TO LAUNCH",
            _ => ""
        };

        if (firstTime)
        {
            if (combo) _comboBox.Text = curValue ?? "";
            if (typeString) _stringBox.Text = curValue ?? "";
            if (media)
            {
                int mi = -1;
                for (int i = 0; i < _mediaCombo.Items.Count; i++)
                {
                    if (_mediaCombo.Items[i] is MediaOption mo
                        && string.Equals(mo.Id, curValue, StringComparison.OrdinalIgnoreCase))
                    {
                        mi = i;
                        break;
                    }
                }
                _mediaCombo.SelectedIndex = mi >= 0 ? mi : 0;
            }
            if (launch) _launchBox.Text = curValue ?? "";
        }
    }

    // ------------------------------------------------------------------
    // Combo capture
    // ------------------------------------------------------------------

    private void ToggleCapture()
    {
        if (_capturing) { StopCapture(); return; }
        _capturing = true;
        _captureButton.Text = "Listening… press now";
        EnsureCaptureTimeout();
        _captureTimeout!.Interval = 8000;
        _captureTimeout.Stop();
        _captureTimeout.Start();
        Focus();
    }

    private static void EnsureCaptureTimeout()
    {
        if (_captureTimeout != null) return;
        _captureTimeout = new System.Windows.Forms.Timer();
        _captureTimeout.Tick += (s, e) =>
        {
            foreach (Form f in Application.OpenForms)
            {
                if (f is KeyMapperDialog kd) { kd.StopCapture(); return; }
            }
        };
    }

    private void StopCapture()
    {
        _capturing = false;
        if (_captureButton.Text != "Capture combo…")
            _captureButton.Text = "Capture combo…";
        _captureTimeout?.Stop();
    }

    protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
    {
        if (!_capturing)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        // Modifier key-down events — swallow and wait for the primary key.
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            e.IsInputKey = true;
            return;
        }

        var mods = Control.ModifierKeys;

        // Bare Esc cancels capture.
        if (e.KeyCode == Keys.Escape && mods == Keys.None)
        {
            StopCapture();
            e.IsInputKey = true;
            return;
        }

        string? name = NameForKey(e.KeyCode);
        if (name == null)
        {
            e.IsInputKey = true; // ignore unmapped keys while capturing
            return;
        }

        var sb = new StringBuilder();
        if (mods.HasFlag(Keys.Control)) sb.Append("Ctrl+");
        if (mods.HasFlag(Keys.Alt)) sb.Append("Alt+");
        if (mods.HasFlag(Keys.Shift)) sb.Append("Shift+");
        if (mods.HasFlag(Keys.LWin) || mods.HasFlag(Keys.RWin)) sb.Append("Win+");
        sb.Append(name);

        _comboBox.Text = sb.ToString();
        StopCapture();
        e.IsInputKey = true;
    }

    private static string? NameForKey(Keys k)
    {
        if (k >= Keys.A && k <= Keys.Z)
            return ((char)('A' + (int)(k - Keys.A))).ToString();
        if (k >= Keys.D0 && k <= Keys.D9)
            return ((char)('0' + (int)(k - Keys.D0))).ToString();
        if (k >= Keys.F1 && k <= Keys.F12)
            return "F" + ((int)(k - Keys.F1) + 1);

        return k switch
        {
            Keys.Enter => "Enter",
            Keys.Space => "Space",
            Keys.Tab => "Tab",
            Keys.Back => "Backspace",
            Keys.Delete => "Delete",
            Keys.Insert => "Insert",
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp => "PageUp",
            Keys.PageDown => "PageDown",
            Keys.PrintScreen => "PrintScreen",
            Keys.CapsLock => "CapsLock",
            _ => null
        };
    }

    // ------------------------------------------------------------------
    // Test + OK
    // ------------------------------------------------------------------

    private void RunTest()
    {
        if (_verbCombo.SelectedItem is not VerbOption opt) return;

        Action? work = opt.Verb switch
        {
            Verb.KeyCombo => () => InputInjector.SendKeyCombo(_comboBox.Text.Trim()),
            Verb.TypeString => () => InputInjector.TypeText(_stringBox.Text),
            Verb.Media => () => InputInjector.PressMedia(_mediaCombo.SelectedItem is MediaOption mo ? mo.Id : ""),
            Verb.Launch => () => InputInjector.Launch(_launchBox.Text.Trim()),
            _ => null
        };
        if (work == null) return;

        Task.Run(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                BeginInvoke((Action)(() => MessageBox.Show(this,
                    "Test failed: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
        });
    }

    private void OnOk()
    {
        if (_verbCombo.SelectedItem is not VerbOption opt) return;

        string? value = opt.Verb switch
        {
            Verb.KeyCombo => _comboBox.Text.Trim(),
            Verb.TypeString => _stringBox.Text,
            Verb.Media => _mediaCombo.SelectedItem is MediaOption mo2 ? mo2.Id : "",
            Verb.Launch => _launchBox.Text.Trim(),
            _ => null
        };

        if ((opt.Verb == Verb.KeyCombo || opt.Verb == Verb.Launch) && string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show(this, "Please enter a value first.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (opt.Verb == Verb.Launch && !TargetPlausible(value!))
        {
            var r = MessageBox.Show(this,
                "\"" + value + "\" doesn't look like an existing app, URL, or command.\r\nSave it anyway?",
                "Launch target check", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        _manager.SetMapping(_preset.Id, _keyId, new KeyMapping(opt.Verb, value));
        DialogResult = DialogResult.OK;
        Close();
    }

    private static bool TargetPlausible(string t)
    {
        try
        {
            if (Uri.TryCreate(t, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https" or "mailto" or "file")
                return true;
            return File.Exists(t) || Directory.Exists(t);
        }
        catch
        {
            return false;
        }
    }
}
