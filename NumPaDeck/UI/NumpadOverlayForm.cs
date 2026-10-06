using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NumPaDeck.Actions;
using NumPaDeck.App;
using NumPaDeck.Hooks;
using NumPaDeck.Presets;

namespace NumPaDeck.UI;

/// <summary>
/// The floating numpad mirror: an always-on-top, borderless, non-taskbar panel
/// showing the active preset name and a per-key action grid for the 17 numpad
/// keys. Translucent (its Opacity comes from settings), draggable from any
/// point, hidden via × (visibility persisted through PresetManager).
/// Pressing a physical numpad key flashes the matching cell.
/// </summary>
public sealed class NumpadOverlayForm : Form
{
    public const int PanelW = 452;
    public const int PanelH = 352;
    public const int CellW = 100;
    public const int CellH = 58;
    public const int Gap = 8;

    private readonly PresetManager _manager;
    private readonly Dictionary<string, OverlayCell> _cells = new();
    private readonly Label _nameLabel;
    private readonly Label _hintLabel;
    private readonly Panel _dot;
    private readonly Button _close;
    private readonly System.Windows.Forms.Timer _flashTimer;
    private readonly EventHandler _onStateChanged;
    private readonly EventHandler _onOverlayChanged;
    private readonly EventHandler<string> _onKeyActivity;

    private string _lastPresetName = string.Empty;
    private bool _flashHeader;
    private bool _dragging;
    private Point _dragStartPos;
    private Point _dragStartLoc;

    /// <summary>Active preset name shown in the header (test hook).</summary>
    internal string? HeaderName { get; private set; }

    /// <summary>Number of cells currently in the "mapped" state (test hook).</summary>
    internal int MappedCellCount { get; private set; }

    /// <summary>Last key flashed by KeyActivity (test hook).</summary>
    internal string? LastFlashedKey { get; private set; }

    public NumpadOverlayForm(PresetManager manager)
    {
        _manager = manager;

        Text = "NumPaDeck — Numpad";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Palette.PanelBg;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(PanelW, PanelH);
        Opacity = Math.Clamp(manager.Settings.Overlay.Opacity, 0.15, 1.0);
        Location = ComputeInitialLocation();

        // --- header: dot · preset name · "active preset" · × ---
        _dot = new Panel { Location = new Point(15, 12), Size = new Size(12, 12) };
        _dot.Paint += Dot_Paint;
        Controls.Add(_dot);

        _nameLabel = new Label
        {
            AutoSize = true,
            Font = Fonts.Name,
            ForeColor = Palette.NameText,
            Location = new Point(36, 9),
            Text = manager.ActivePreset?.Name ?? string.Empty
        };
        Controls.Add(_nameLabel);

        _hintLabel = new Label
        {
            AutoSize = true,
            Font = Fonts.Hint,
            ForeColor = Palette.Hint,
            Text = "active preset",
            Location = new Point(_nameLabel.Right + 6, 13)
        };
        Controls.Add(_hintLabel);

        _close = new Button
        {
            Text = "\u2715",
            Location = new Point(PanelW - 14 - 24, 8),
            Size = new Size(24, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = Palette.CloseBg,
            ForeColor = Palette.CloseGlyph,
            Font = new Font("Segoe UI", 8.5f),
            TabStop = false
        };
        _close.FlatAppearance.BorderColor = Palette.CloseBorder;
        _close.FlatAppearance.BorderSize = 1;
        _close.Click += (s, e) => _manager.SetOverlay(false, _manager.Settings.Overlay.Opacity);
        Controls.Add(_close);

        BuildCells();
        BuildLegend();

        _flashTimer = new System.Windows.Forms.Timer { Interval = 340 };
        _flashTimer.Tick += OnFlashTimer;

        _onStateChanged = (s, e) =>
        {
            try
            {
                Opacity = Math.Clamp(_manager.Settings.Overlay.Opacity, 0.15, 1.0);
                RefreshFromState();
            }
            catch (Exception ex) { Log.Error("Overlay refresh failed: " + ex.Message); }
        };
        _onOverlayChanged = (s, e) =>
        {
            try { Opacity = Math.Clamp(_manager.Settings.Overlay.Opacity, 0.15, 1.0); }
            catch (Exception ex) { Log.Error("Overlay opacity update failed: " + ex.Message); }
        };
        _onKeyActivity = (s, key) =>
        {
            try { FlashKey(key); }
            catch (Exception ex) { Log.Error("Overlay flash failed: " + ex.Message); }
        };

        _manager.StateChanged += _onStateChanged;
        _manager.OverlayChanged += _onOverlayChanged;
        _manager.KeyActivity += _onKeyActivity;

        FormClosed += OnFormClosed;

        // Establish the baseline name so the first paint doesn't pulse.
        _lastPresetName = manager.ActivePreset?.Name ?? string.Empty;
        RefreshFromState();
    }

    // ------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------

    private void BuildCells()
    {
        int[] rowY = { 50, 116, 182, 248 };
        int[] colX = { 14, 122, 230, 338 };
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                string id = NumpadKeys.GridRows[row * 4 + col];
                if (id == "0" && col != 0) continue; // "0" spans the first two cells of the last row
                _cells[id] = new OverlayCell(id)
                {
                    Location = new Point(colX[col], rowY[row]),
                    Size = id == "0" ? new Size(CellW * 2 + Gap, CellH) : new Size(CellW, CellH)
                };
                Controls.Add(_cells[id]);
            }
        }
    }

    private void BuildLegend()
    {
        AddLegend(16, "Mapped", Palette.MappedBack, Palette.MappedBorder, false);
        AddLegend(108, "Gesture key", Palette.GestureBack, Palette.GestureBorder, false);
        AddLegend(232, "Suppressed", Palette.SuppressedBack, Palette.SuppressedBorder, true);
        AddLegend(344, "Passthrough", Palette.PassBack, Palette.PassBorder, false);
    }

    private void AddLegend(int x, string text, Color back, Color border, bool dashed)
    {
        var chip = new Panel { Location = new Point(x, 323), Size = new Size(11, 11) };
        chip.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(new Rectangle(0, 0, 11, 11), 3);
            using (var b = new SolidBrush(back)) e.Graphics.FillPath(b, path);
            using (var pen = new Pen(border, 1f) { DashStyle = dashed ? DashStyle.Dash : DashStyle.Solid })
                e.Graphics.DrawPath(pen, path);
        };
        Controls.Add(chip);
        Controls.Add(new Label
        {
            Text = text,
            Font = Fonts.Legend,
            ForeColor = Palette.LegendText,
            AutoSize = true,
            Location = new Point(x + 16, 320)
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Palette.Frame, 1f);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        e.Graphics.DrawLine(pen, 14, 314, ClientSize.Width - 14, 314);
    }

    private void Dot_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(_flashHeader ? Palette.DotFlash : Palette.Dot);
        e.Graphics.FillEllipse(b, 0, 0, _dot.Width, _dot.Height);
    }

    // ------------------------------------------------------------------
    // State -> visuals
    // ------------------------------------------------------------------

    /// <summary>Re-read the active preset + mappings and repaint the grid.</summary>
    public void RefreshFromState()
    {
        var preset = _manager.ActivePreset;
        string name = preset?.Name ?? string.Empty;
        bool presetChanged = name != _lastPresetName;
        _lastPresetName = name;

        // Keep the name from clobbering the × button.
        string shown = name;
        int limit = 352;
        while (shown.Length > 3 && TextRenderer.MeasureText(shown, Fonts.Name).Width > limit)
            shown = shown[..^1] + "\u2026";
        if (shown != _nameLabel.Text) _nameLabel.Text = shown;
        _hintLabel.Location = new Point(_nameLabel.Right + 6, 13);
        _hintLabel.Visible = _nameLabel.Right + 6 < PanelW - 64;

        bool gestureOn = _manager.Settings.Gesture.Enabled;
        string gestureKey = gestureOn ? (_manager.Settings.Gesture.Key ?? string.Empty) : string.Empty;

        int mapped = 0;
        foreach (var kv in _cells)
        {
            var cell = kv.Value;
            bool isGesture = gestureOn
                && gestureKey.Length > 0
                && string.Equals(kv.Key, gestureKey, StringComparison.OrdinalIgnoreCase);

            KeyMapping? m = null;
            if (preset != null && preset.Mappings.TryGetValue(kv.Key, out var mm)) m = mm;
            var eff = m?.Verb ?? Verb.Passthrough;

            cell.Kind = isGesture ? OverlayCell.CellKind.Gesture
                       : eff switch
                       {
                           Verb.None => OverlayCell.CellKind.Suppressed,
                           Verb.Passthrough => OverlayCell.CellKind.Passthrough,
                           _ => OverlayCell.CellKind.Mapped
                       };
            cell.Action = isGesture ? "double-tap \u2192 cycle preset"
                         : eff switch
                         {
                             Verb.None => "\u2715 suppressed",
                             Verb.Passthrough => string.Empty,
                             _ => DescribeShort(m!)
                         };
            if (cell.Kind == OverlayCell.CellKind.Mapped) mapped++;
            cell.Invalidate();
        }
        MappedCellCount = mapped;
        HeaderName = name;

        if (presetChanged)
        {
            _flashHeader = true;
            _dot.Invalidate();
            _flashTimer.Stop();
            _flashTimer.Start();
        }
    }

    private static string DescribeShort(KeyMapping m)
    {
        string v = m.Value ?? string.Empty;
        string t = v.Length <= 16 ? v : v[..14] + "\u2026";
        return m.Verb switch
        {
            Verb.KeyCombo => t,
            Verb.TypeString => t,
            Verb.Media => MediaAction.LabelOf(v) ?? t,
            Verb.Launch => t,
            _ => string.Empty
        };
    }

    // ------------------------------------------------------------------
    // Key-press flash
    // ------------------------------------------------------------------

    private void FlashKey(string keyId)
    {
        if (!_cells.TryGetValue(keyId, out var cell)) return;
        foreach (var kv in _cells)
        {
            bool want = ReferenceEquals(kv.Value, cell);
            if (kv.Value.Flash != want)
            {
                kv.Value.Flash = want;
                kv.Value.Invalidate();
            }
        }
        LastFlashedKey = keyId;
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    private void OnFlashTimer(object? sender, EventArgs e)
    {
        _flashTimer.Stop();
        foreach (var kv in _cells)
            if (kv.Value.Flash)
            {
                kv.Value.Flash = false;
                kv.Value.Invalidate();
            }
        if (_flashHeader)
        {
            _flashHeader = false;
            _dot.Invalidate();
        }
    }

    // ------------------------------------------------------------------
    // Dragging (from any point on the panel) + remembered position
    // ------------------------------------------------------------------

    public void BeginDrag()
    {
        if (_dragging) return;
        _dragging = true;
        _dragStartPos = Cursor.Position;
        _dragStartLoc = Location;
        try { Capture = true; } catch { /* ignore */ }
    }

    public void DragMove()
    {
        if (!_dragging) return;
        Point at = Cursor.Position;
        Location = new Point(
            _dragStartLoc.X + at.X - _dragStartPos.X,
            _dragStartLoc.Y + at.Y - _dragStartPos.Y);
    }

    public void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        try { Capture = false; } catch { /* ignore */ }
        bool moved = Math.Abs(Cursor.Position.X - _dragStartPos.X) > 2
                  || Math.Abs(Cursor.Position.Y - _dragStartPos.Y) > 2;
        if (!moved) return;
        PersistPosition();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) BeginDrag();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        DragMove();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) EndDrag();
    }

    /// <summary>Clamp to a visible screen and persist the position (test hook too).</summary>
    internal void PersistPosition()
    {
        Point clamped = ClampToScreen(Size, Location);
        if (clamped != Location) Location = clamped;
        try
        {
            _manager.SetOverlayPosition(Location.X, Location.Y);
        }
        catch (Exception ex) { Log.Error("Overlay: saving position failed: " + ex.Message); }
    }

    private Point ComputeInitialLocation()
    {
        int? sx = _manager.Settings.Overlay.X;
        int? sy = _manager.Settings.Overlay.Y;
        if (!sx.HasValue || !sy.HasValue)
        {
            // Default: bottom-right of the primary work area.
            if (Screen.PrimaryScreen is { } screen)
            {
                var wa = screen.WorkingArea;
                return new Point(wa.Right - PanelW - 24, wa.Bottom - PanelH - 24);
            }
            return new Point(0, 0);
        }
        return ClampToScreen(new Size(PanelW, PanelH), new Point(sx.Value, sy.Value));
    }

    internal static Point ClampToScreen(Size formSize, Point p)
    {
        foreach (var s in Screen.AllScreens)
        {
            if (s.Bounds.Contains(p))
            {
                var wa = s.WorkingArea;
                int x = Math.Max(wa.Left, Math.Min(p.X, wa.Right - formSize.Width));
                int y = Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - formSize.Height));
                return new Point(x, y);
            }
        }
        if (Screen.PrimaryScreen is { } fallback)
        {
            var wa = fallback.WorkingArea;
            return new Point(wa.Right - formSize.Width - 24, wa.Bottom - formSize.Height - 24);
        }
        return p;
    }

    // ------------------------------------------------------------------

    /// <summary>State of a cell (test hook).</summary>
    internal OverlayCell.CellKind? KindOf(string keyId) =>
        _cells.TryGetValue(keyId, out var c) ? c.Kind : null;

    /// <summary>Action caption of a cell (test hook).</summary>
    internal string? ActionOf(string keyId) =>
        _cells.TryGetValue(keyId, out var c) ? c.Action : null;

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _manager.StateChanged -= _onStateChanged;
        _manager.OverlayChanged -= _onOverlayChanged;
        _manager.KeyActivity -= _onKeyActivity;
        _flashTimer.Stop();
        _flashTimer.Dispose();
    }

    // ------------------------------------------------------------------
    // Paint helpers
    // ------------------------------------------------------------------

    internal static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Mirrors the mockup: Mapped / Gesture / Suppressed / Passthrough.</summary>
    internal static class Palette
    {
        public static readonly Color PanelBg = Color.FromArgb(18, 22, 32);
        public static readonly Color Frame = Color.FromArgb(72, 80, 98);
        public static readonly Color NameText = Color.FromArgb(242, 244, 248);
        public static readonly Color Hint = Color.FromArgb(168, 178, 196);
        public static readonly Color Dot = Color.FromArgb(77, 163, 255);
        public static readonly Color DotFlash = Color.FromArgb(225, 240, 255);
        public static readonly Color CloseBg = Color.FromArgb(46, 52, 66);
        public static readonly Color CloseBorder = Color.FromArgb(86, 94, 110);
        public static readonly Color CloseGlyph = Color.FromArgb(223, 229, 238);
        public static readonly Color PassBack = Color.FromArgb(40, 46, 60);
        public static readonly Color PassBorder = Color.FromArgb(104, 113, 130);
        public static readonly Color PassGlyph = Color.FromArgb(150, 160, 177);
        public static readonly Color MappedBack = Color.FromArgb(27, 71, 126);
        public static readonly Color MappedBorder = Color.FromArgb(88, 145, 215);
        public static readonly Color MappedGlyph = Color.White;
        public static readonly Color MappedAction = Color.FromArgb(205, 222, 255);
        public static readonly Color GestureBack = Color.FromArgb(137, 95, 21);
        public static readonly Color GestureBorder = Color.FromArgb(216, 167, 62);
        public static readonly Color GestureGlyph = Color.FromArgb(253, 243, 221);
        public static readonly Color GestureAction = Color.FromArgb(239, 214, 163);
        public static readonly Color SuppressedBack = Color.FromArgb(36, 41, 55);
        public static readonly Color SuppressedBorder = Color.FromArgb(122, 131, 148);
        public static readonly Color SuppressedGlyph = Color.FromArgb(174, 182, 197);
        public static readonly Color SuppressedAction = Color.FromArgb(184, 192, 206);
        public static readonly Color FlashBack = Color.FromArgb(62, 118, 198);
        public static readonly Color FlashBorder = Color.FromArgb(166, 203, 255);
        public static readonly Color LegendText = Color.FromArgb(195, 203, 217);
    }

    internal static class Fonts
    {
        public static readonly Font Name = new("Segoe UI", 11f, FontStyle.Bold);
        public static readonly Font Hint = new("Segoe UI", 8.5f);
        public static readonly Font Legend = new("Segoe UI", 8.5f);
        public static readonly Font Glyph = new("Segoe UI", 11f, FontStyle.Bold);
        public static readonly Font Action = new("Segoe UI", 7.5f);
    }

    // ------------------------------------------------------------------
    // The key cell
    // ------------------------------------------------------------------

    internal sealed class OverlayCell : Panel
    {
        public enum CellKind { Mapped, Gesture, Suppressed, Passthrough }

        public string Id { get; }
        public string Glyph { get; }
        [DefaultValue(""), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public string Action { get; set; } = string.Empty;

        [DefaultValue(typeof(CellKind), "Passthrough"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public CellKind Kind { get; set; } = CellKind.Passthrough;

        [DefaultValue(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public bool Flash { get; set; }

        public OverlayCell(string id)
        {
            Id = id;
            Glyph = NumpadKeys.Display(id);
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                (FindForm() as NumpadOverlayForm)?.BeginDrag();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color back = Palette.PassBack;
            Color border = Palette.PassBorder;
            Color glyph = Palette.PassGlyph;
            Color action = Palette.Hint;
            switch (Kind)
            {
                case CellKind.Mapped:
                    back = Palette.MappedBack; border = Palette.MappedBorder;
                    glyph = Palette.MappedGlyph; action = Palette.MappedAction;
                    break;
                case CellKind.Gesture:
                    back = Palette.GestureBack; border = Palette.GestureBorder;
                    glyph = Palette.GestureGlyph; action = Palette.GestureAction;
                    break;
                case CellKind.Suppressed:
                    back = Palette.SuppressedBack; border = Palette.SuppressedBorder;
                    glyph = Palette.SuppressedGlyph; action = Palette.SuppressedAction;
                    break;
            }
            if (Flash)
            {
                back = Palette.FlashBack;
                border = Palette.FlashBorder;
            }

            using (var path = NumpadOverlayForm.RoundedRect(new Rectangle(0, 0, Width, Height), 9))
            {
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
                using (var pen = new Pen(border, Flash ? 1.5f : 1f)
                    { DashStyle = Kind == CellKind.Suppressed ? DashStyle.Dash : DashStyle.Solid })
                {
                    g.DrawPath(pen, path);
                }
            }

            const TextFormatFlags center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            if (Action.Length > 0)
            {
                int split = Height * 55 / 100;
                TextRenderer.DrawText(g, Glyph, Fonts.Glyph,
                    new Rectangle(2, 0, Width - 4, split), glyph, center | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, Action, Fonts.Action,
                    new Rectangle(2, split, Width - 4, Height - split - 1),
                    action, center | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            else
            {
                TextRenderer.DrawText(g, Glyph, Fonts.Glyph,
                    new Rectangle(2, 1, Width - 4, Height - 2), glyph, center | TextFormatFlags.NoPadding);
            }
        }
    }
}
