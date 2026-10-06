using System.Drawing;
using System.Windows.Forms;
using NumPaDeck.Actions;
using NumPaDeck.App;
using NumPaDeck.Hooks;
using NumPaDeck.Presets;

namespace NumPaDeck.UI;

/// <summary>
/// Settings window: preset list + numpad mapping grid + legend + overlay row
/// (show numpad overlay + opacity) + bottom bar (enable toggle, gesture config,
/// Save/Cancel). Edits apply to live state immediately but are batched: Save
/// commits + persists, Cancel restores the snapshot taken when the window opened.
/// </summary>
public sealed class SettingsForm : Form
{
    private const int CellW = 108;
    private const int CellH = 88;
    private const int Gap = 8;
    private const int GridX = 272;
    private const int GridY = 44;

    private readonly PresetManager _manager;
    private readonly Dictionary<string, NumpadCellControl> _cells = new();
    private Guid _selectedId;
    private bool _endedCleanly;

    private readonly ListBox _presetList;
    private readonly Label _gridHeader;
    private readonly CheckBox _enableBox;
    private readonly CheckBox _gestureBox;
    private readonly ComboBox _gestureKeyCombo;
    private readonly ComboBox _windowCombo;
    private CheckBox _overlayBox;
    private TrackBar _opacityBar;
    private Label _opacityPct;
    private bool _syncingOverlay;

    private static readonly (string Id, string Label)[] GestureKeys =
    {
        ("7","7"), ("8","8"), ("9","9"), ("sub", "−"),
        ("4","4"), ("5","5"), ("6","6"), ("add", "+"),
        ("1","1"), ("2","2"), ("3","3"), ("equal","="),
        ("0","0"), ("dot","."), ("enter","Enter"),
        ("div","÷"), ("mul","×")
    };

    private sealed class GestureKeyOption
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
        public override string ToString() => Label;
    }

    public SettingsForm(PresetManager manager)
    {
        _manager = manager;

        Text = "NumPaDeck — Settings";
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(962, 648);

        _presetList = new ListBox
        {
            Location = new Point(24, 40),
            Size = new Size(220, 300),
            DrawMode = DrawMode.OwnerDrawFixed,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10.5f)
        };
        _presetList.DrawItem += PresetList_DrawItem;
        _presetList.SelectedIndexChanged += (s, e) =>
        {
            if (_presetList.SelectedItem is Guid g)
            {
                _selectedId = g;
                RefreshGrid();
            }
        };
        _presetList.DoubleClick += (s, e) =>
        {
            if (_presetList.SelectedItem is not Guid g) return;
            var p = _manager.GetPreset(g);
            if (p == null) return;
            _manager.SetActivePreset(g);
            Toast.Show("Active preset: " + p.Name);
            RefreshAll();
        };

        BuildLeftPanel();
        _gridHeader = BuildNumpadGrid();
        BuildLegend();
        _enableBox = BuildBottomBar(out _gestureBox, out _gestureKeyCombo, out _windowCombo);
        (_overlayBox, _opacityBar, _opacityPct) = BuildOverlayRow();

        _manager.BeginEdit();

        var active = _manager.ActivePreset;
        _selectedId = active?.Id ?? _manager.Settings.Presets.FirstOrDefault()?.Id ?? Guid.Empty;

        Shown += (s, e) => _manager.Suspend();   // don't fire numpad mappings while editing
        FormClosing += (s, e) =>
        {
            if (!_endedCleanly)
            {
                _endedCleanly = true;
                _manager.EndEdit(false); // treat X as Cancel
            }
        };

        RefreshAll();
    }

    // ------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------

    private void BuildLeftPanel()
    {
        Controls.Add(new Label
        {
            Text = "PRESETS",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(120, 125, 130),
            AutoSize = true,
            Location = new Point(24, 16)
        });

        Controls.Add(_presetList);

        var newB = MakeSmallButton("New", new Point(24, 352), (s, e) =>
        {
            var p = _manager.CreatePreset("New preset");
            SelectPreset(p.Id);
        });
        var dupB = MakeSmallButton("Duplicate", new Point(140, 352), (s, e) =>
        {
            var p = _manager.DuplicatePreset(_selectedId);
            if (p != null) SelectPreset(p.Id);
        });
        var renB = MakeSmallButton("Rename", new Point(24, 390), (s, e) =>
        {
            var cur = _manager.GetPreset(_selectedId);
            if (cur == null) return;
            var f = new PromptForm("Rename preset", "Preset name:", cur.Name) { Owner = this };
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                _manager.RenamePreset(_selectedId, f.Value);
                RefreshAll();
            }
        });
        var delB = MakeSmallButton("Delete", new Point(140, 390), (s, e) =>
        {
            var cur = _manager.GetPreset(_selectedId);
            if (cur == null) return;
            bool isActive = _manager.ActivePreset?.Id == cur.Id;
            string msg = (isActive ? "This is the active preset — " : "") + "Delete \"" + cur.Name + "\"?";
            if (MessageBox.Show(this, msg, "Delete preset", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes)
                return;
            if (_manager.DeletePreset(_selectedId))
            {
                var first = _manager.Settings.Presets.FirstOrDefault();
                if (first != null) _selectedId = first.Id;
                RefreshAll();
                Toast.Show("Preset deleted");
            }
        });
        Controls.Add(newB);
        Controls.Add(dupB);
        Controls.Add(renB);
        Controls.Add(delB);

        Controls.Add(new Label
        {
            Text = "Double-click a preset to make it active.\r\nClick any numpad key to map it.",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(130, 135, 140),
            Location = new Point(24, 432),
            Size = new Size(224, 44)
        });
    }

    private static Button MakeSmallButton(string text, Point location, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            Location = location,
            Size = new Size(104, 30),
            FlatStyle = FlatStyle.System
        };
        b.Click += onClick;
        return b;
    }

    private Label BuildNumpadGrid()
    {
        var header = new Label
        {
            Text = "NUMPAD",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(120, 125, 130),
            AutoSize = true,
            Location = new Point(GridX, 16)
        };
        Controls.Add(header);

        Point Pos(int col, int row) => new(GridX + col * (CellW + Gap), GridY + row * (CellH + Gap));

        void AddCell(string id, int col, int row, int span)
        {
            var cell = new NumpadCellControl
            {
                Location = Pos(col, row),
                Size = span > 1 ? new Size(CellW * span + Gap * (span - 1), CellH) : new Size(CellW, CellH),
                Glyph = NumpadKeys.Display(id)
            };
            cell.CellClicked += (s, e) => OnCellClicked(id);
            _cells[id] = cell;
            Controls.Add(cell);
        }

        AddCell("7", 0, 0, 1); AddCell("8", 1, 0, 1); AddCell("9", 2, 0, 1); AddCell("sub", 3, 0, 1);
        AddCell("4", 0, 1, 1); AddCell("5", 1, 1, 1); AddCell("6", 2, 1, 1); AddCell("add", 3, 1, 1);
        AddCell("1", 0, 2, 1); AddCell("2", 1, 2, 1); AddCell("3", 2, 2, 1); AddCell("equal", 3, 2, 1);
        AddCell("0", 0, 3, 2);  AddCell("dot", 2, 3, 1); AddCell("enter", 3, 3, 1);

        // Extra row for the two keys that don't fit the classic 4x4 layout.
        Controls.Add(new Label
        {
            Text = "EXTRA NUMPAD KEYS",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(140, 145, 150),
            AutoSize = true,
            Location = new Point(GridX, 428)
        });
        AddCell("div", 0, 5, 1);
        _cells["div"].Location = new Point(GridX, 452);
        _cells["div"].Size = new Size(96, 52);
        AddCell("mul", 0, 6, 1);
        _cells["mul"].Location = new Point(GridX + 104, 452);
        _cells["mul"].Size = new Size(96, 52);

        return header;
    }

    private void BuildLegend()
    {
        const int x = 752;
        Controls.Add(new Label
        {
            Text = "LEGEND",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(120, 125, 130),
            AutoSize = true,
            Location = new Point(x, 16)
        });
        AddChip(x, 46, Color.FromArgb(230, 240, 250), Color.FromArgb(0, 103, 192), false, "Mapped key", "has an action");
        AddChip(x, 86, Color.FromArgb(253, 243, 224), Color.FromArgb(138, 90, 0), false, "Gesture key", "double-tap switches preset");
        AddChip(x, 126, Color.FromArgb(242, 242, 242), Color.FromArgb(170, 170, 170), true, "Do nothing", "key is swallowed");
        AddChip(x, 166, Color.White, Color.FromArgb(210, 212, 216), false, "Passthrough", "keeps normal behavior");

        Controls.Add(new Label
        {
            Text = "Single tap still does the key's mapping. A double tap on the gesture key switches preset.",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(130, 135, 140),
            Location = new Point(x, 214),
            Size = new Size(196, 60)
        });
    }

    private void AddChip(int x, int y, Color back, Color border, bool dashed, string title, string sub)
    {
        var chip = new Panel { Location = new Point(x, y), Size = new Size(18, 18), BackColor = back };
        chip.Paint += (s, e) =>
        {
            using var pen = new System.Drawing.Pen(border, 1.4f);
            if (dashed) pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
            e.Graphics.DrawRectangle(pen, 1, 1, 14, 14);
        };
        Controls.Add(chip);
        Controls.Add(new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Location = new Point(x + 28, y - 3),
            AutoSize = true
        });
        Controls.Add(new Label
        {
            Text = sub,
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(130, 135, 140),
            Location = new Point(x + 28, y + 15),
            AutoSize = true
        });
    }

    private CheckBox BuildBottomBar(out CheckBox gestureBox, out ComboBox keyCombo, out ComboBox windowCombo)
    {
        var enable = new CheckBox
        {
            Text = "Enable numpad hijacking",
            Location = new Point(24, 600),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        gestureBox = new CheckBox
        {
            Text = "Double-tap switch:",
            Location = new Point(216, 600),
            AutoSize = true
        };

        keyCombo = new ComboBox
        {
            Location = new Point(352, 596),
            Size = new Size(84, 26),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var (id, label) in GestureKeys)
            keyCombo.Items.Add(new GestureKeyOption { Id = id, Label = label });

        Controls.Add(new Label
        {
            Text = "within",
            Location = new Point(446, 600),
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 95, 100)
        });
        windowCombo = new ComboBox
        {
            Location = new Point(500, 596),
            Size = new Size(70, 26),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (int ms in new[] { 100, 150, 200, 250, 300, 400, 500, 750, 1000 })
            windowCombo.Items.Add(ms);

        var cancel = new Button { Text = "Cancel", Location = new Point(766, 592), Size = new Size(80, 34) };
        var save = new Button
        {
            Text = "Save",
            Location = new Point(854, 592),
            Size = new Size(84, 34),
            BackColor = Color.FromArgb(0, 103, 192),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        save.FlatAppearance.BorderSize = 0;

        enable.CheckedChanged += (s, e) => _manager.SetEnabled(enable.Checked);
        gestureBox.CheckedChanged += (s, e) => ApplyGestureSettings();
        keyCombo.SelectedIndexChanged += (s, e) => ApplyGestureSettings();
        windowCombo.SelectedIndexChanged += (s, e) => ApplyGestureSettings();

        cancel.Click += (s, e) =>
        {
            _endedCleanly = true;
            _manager.EndEdit(false);
            Close();
        };
        save.Click += (s, e) =>
        {
            _endedCleanly = true;
            _manager.EndEdit(true);
            Toast.Show("Settings saved");
            Close();
        };

        Controls.Add(enable);
        Controls.Add(gestureBox);
        Controls.Add(keyCombo);
        Controls.Add(windowCombo);
        Controls.Add(cancel);
        Controls.Add(save);
        return enable;
    }

    private void ApplyGestureSettings()
    {
        if (_gestureKeyCombo.SelectedItem is not GestureKeyOption opt) return;
        if (_windowCombo.SelectedItem is not int ms) return;
        _manager.SetGesture(_gestureBox.Checked, opt.Id, ms);
    }

    private (CheckBox Box, TrackBar Bar, Label Pct) BuildOverlayRow()
    {
        var box = new CheckBox
        {
            Text = "Show numpad overlay",
            Location = new Point(24, 530),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        Controls.Add(new Label
        {
            Text = "Opacity",
            Location = new Point(186, 532),
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 95, 100)
        });

        var bar = new TrackBar
        {
            Location = new Point(248, 518),
            Size = new Size(220, 45),
            Minimum = 15,
            Maximum = 100,
            TickStyle = TickStyle.None
        };

        var pct = new Label
        {
            Text = "… %",
            Location = new Point(484, 532),
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        box.CheckedChanged += (s, e) => ApplyOverlaySettings();
        bar.ValueChanged += (s, e) => ApplyOverlaySettings();

        Controls.Add(box);
        Controls.Add(bar);
        Controls.Add(pct);
        return (box, bar, pct);
    }

    private void ApplyOverlaySettings()
    {
        if (_syncingOverlay) return; // programmatic sync in RefreshBottomBar, not user input
        _opacityPct.Text = _opacityBar.Value + " %";
        _manager.SetOverlay(_overlayBox.Checked, _opacityBar.Value / 100.0);
    }

    // ------------------------------------------------------------------
    // Grid + list refresh
    // ------------------------------------------------------------------

    private void OnCellClicked(string keyId)
    {
        var preset = _manager.GetPreset(_selectedId);
        if (preset == null) return;
        using var dlg = new KeyMapperDialog(_manager, preset, keyId) { Owner = this };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            RefreshGrid();
    }

    private void RefreshAll()
    {
        RefreshPresetList();
        RefreshGrid();
        RefreshBottomBar();
    }

    private void SelectPreset(Guid id)
    {
        _selectedId = id;
        RefreshPresetList();
        RefreshGrid();
    }

    private void RefreshPresetList()
    {
        _presetList.BeginUpdate();
        _presetList.Items.Clear();
        foreach (var p in _manager.Settings.Presets)
            _presetList.Items.Add(p.Id);
        _presetList.EndUpdate();

        int idx = -1;
        for (int i = 0; i < _manager.Settings.Presets.Count; i++)
            if (_manager.Settings.Presets[i].Id == _selectedId) { idx = i; break; }
        _presetList.SelectedIndex = idx;
    }

    private void PresetList_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _presetList.Items[e.Index] is not Guid id) return;
        var p = _manager.GetPreset(id);
        if (p == null) return;

        bool active = p.Id == _manager.ActivePreset?.Id;
        e.DrawBackground();
        using var textBrush = new SolidBrush(e.State == DrawItemState.Selected ? Color.White : Color.FromArgb(30, 32, 36));
        if (e.Font is { } font)
        {
            // Active preset: bold text + a distinctly larger dot, so "active" is unmissable.
            using var bold = active ? new Font(font, FontStyle.Bold) : null;
            e.Graphics.DrawString(p.Name, active && bold != null ? bold : font, textBrush,
                e.Bounds.Left + (active ? 22 : 6), e.Bounds.Top + 3);
        }
        if (active)
        {
            using var dot = new SolidBrush(e.State == DrawItemState.Selected ? Color.White : Color.FromArgb(0, 103, 192));
            e.Graphics.FillEllipse(dot, e.Bounds.Left + 4, e.Bounds.Bottom - 12, 8, 8);
        }
    }

    private void RefreshGrid()
    {
        var preset = _manager.GetPreset(_selectedId);
        if (preset == null) return;

        bool isActive = _manager.ActivePreset?.Id == preset.Id;
        _gridHeader.Text = "NUMPAD — preset " + "\"" + preset.Name + "\""
            + (isActive
                ? "    ·    ● ACTIVE — mappings are live"
                : "    ·    ○ NOT ACTIVE — mappings won't apply until you double-click this preset in the list")
            + "     ·     click a key to map it";

        bool gestureOn = _manager.Settings.Gesture.Enabled;
        string gestureKey = gestureOn ? (_manager.Settings.Gesture.Key ?? "") : "";

        foreach (var kv in _cells)
        {
            bool isGesture = gestureOn
                && !string.IsNullOrEmpty(gestureKey)
                && string.Equals(kv.Key, gestureKey, StringComparison.OrdinalIgnoreCase);

            KeyMapping? m = preset.Mappings.TryGetValue(kv.Key, out var mm) ? mm : null;
            var eff = m?.Verb ?? Verb.Passthrough;

            kv.Value.Style = isGesture
                ? CellStyle.Gesture
                : eff switch
                {
                    Verb.None => CellStyle.None,
                    Verb.Passthrough => CellStyle.Passthrough,
                    _ => CellStyle.Mapped
                };

            kv.Value.Status = isGesture
                ? "DOUBLE-TAP" + (eff != Verb.Passthrough && eff != Verb.None
                    ? " · " + VerbWord(eff)
                    : "")
                : eff == Verb.Passthrough ? "" : Describe(m!);

            kv.Value.Invalidate();
        }
    }

    private static string VerbWord(Verb v) => v switch
    {
        Verb.KeyCombo => "COMBO",
        Verb.TypeString => "TYPE",
        Verb.Media => "MEDIA",
        Verb.Launch => "LAUNCH",
        Verb.None => "NONE",
        _ => ""
    };

    private static string Describe(KeyMapping m) => m.Verb switch
    {
        Verb.None => "DO NOTHING",
        Verb.KeyCombo => "COMBO · " + Trunc(m.Value),
        Verb.TypeString => "TYPE · " + Trunc(m.Value),
        Verb.Media => "MEDIA · " + (MediaAction.LabelOf(m.Value) ?? Trunc(m.Value)),
        Verb.Launch => "LAUNCH · " + Trunc(m.Value),
        _ => ""
    };

    private static string Trunc(string? s)
    {
        s ??= "";
        return s.Length <= 17 ? s : s[..15] + "…";
    }

    private void RefreshBottomBar()
    {
        _enableBox.Checked = _manager.Settings.Enabled;
        _gestureBox.Checked = _manager.Settings.Gesture.Enabled;

        var gs = _manager.Settings.Gesture;

        int ki = 0;
        for (int i = 0; i < _gestureKeyCombo.Items.Count; i++)
        {
            if (_gestureKeyCombo.Items[i] is GestureKeyOption gko
                && string.Equals(gko.Id, gs.Key, StringComparison.OrdinalIgnoreCase))
            {
                ki = i;
                break;
            }
        }
        _gestureKeyCombo.SelectedIndex = ki;

        int wi = -1;
        for (int i = 0; i < _windowCombo.Items.Count; i++)
        {
            if (_windowCombo.Items[i] is int iv && iv == gs.TapWindowMs) { wi = i; break; }
        }
        if (wi < 0) _windowCombo.SelectedItem = 250; // fall back to a known entry
        else _windowCombo.SelectedIndex = wi;

        _syncingOverlay = true;
        try
        {
            _overlayBox.Checked = _manager.Settings.Overlay.Visible;
            _opacityBar.Value = Math.Clamp((int)Math.Round(_manager.Settings.Overlay.Opacity * 100.0), 15, 100);
            _opacityPct.Text = _opacityBar.Value + " %";
        }
        finally
        {
            _syncingOverlay = false;
        }
    }
}
