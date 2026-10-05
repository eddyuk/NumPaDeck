using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NumPaDeck.UI;

public enum CellStyle
{
    Passthrough, // default OS behavior (no mapping stored)
    Mapped,      // has an action assigned
    Gesture,     // double-tap preset-switch key
    None         // assigned "do nothing"
}

/// <summary>
/// A single numpad key cell in the settings grid: big glyph + status line,
/// tinted by its state (mapped / gesture / none / passthrough).
/// </summary>
internal sealed class NumpadCellControl : Control
{
    private bool _hot;
    private bool _down;

    [DefaultValue(""), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Glyph { get; set; } = "";

    [DefaultValue(""), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Status { get; set; } = "";

    [DefaultValue(typeof(CellStyle), "Passthrough"), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public CellStyle Style { get; set; } = CellStyle.Passthrough;

    public event EventHandler? CellClicked;

    public NumpadCellControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Cursor = Cursors.Hand;
        BackColor = Color.White;
    }

    private static (Color border, Color back, bool dashed) PaletteFor(CellStyle s, bool hot) => s switch
    {
        CellStyle.Mapped => (Color.FromArgb(0, 103, 192), hot ? Color.FromArgb(210, 229, 248) : Color.FromArgb(230, 240, 250), false),
        CellStyle.Gesture => (Color.FromArgb(138, 90, 0), hot ? Color.FromArgb(247, 226, 178) : Color.FromArgb(253, 243, 224), false),
        CellStyle.None => (Color.FromArgb(170, 170, 170), hot ? Color.FromArgb(232, 232, 232) : Color.FromArgb(242, 242, 242), true),
        _ => (Color.FromArgb(210, 212, 216), hot ? Color.FromArgb(240, 241, 242) : Color.White, false)
    };

    private static (Color text, Color status) TextColorsFor(CellStyle s) => s switch
    {
        CellStyle.Mapped => (Color.FromArgb(18, 32, 48), Color.FromArgb(0, 90, 155)),
        CellStyle.Gesture => (Color.FromArgb(51, 35, 0), Color.FromArgb(138, 90, 0)),
        CellStyle.None => (Color.FromArgb(110, 115, 120), Color.FromArgb(135, 140, 145)),
        _ => (Color.FromArgb(180, 183, 187), Color.FromArgb(198, 201, 205))
    };

    protected override void OnMouseEnter(EventArgs e)
    {
        _hot = true; Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = false; Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _down = true;
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool pressed = _down;
        _down = false;
        base.OnMouseUp(e);
        if (pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            CellClicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var (border, back, dashed) = PaletteFor(Style, _hot);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

        using (var path = Rounded(rect))
        {
            using (var fill = new SolidBrush(back)) g.FillPath(fill, path);
            using (var pen = new Pen(border, Style == CellStyle.Passthrough ? 1f : 1.6f)
            {
                DashStyle = dashed ? DashStyle.Dash : DashStyle.Solid
            })
            {
                g.DrawPath(pen, path);
            }
        }

        var (textColor, statusColor) = TextColorsFor(Style);
        bool showStatus = Status.Length > 0;

        if (Glyph.Length > 0)
        {
            using var glyphFont = new Font("Segoe UI",
                Height >= 72 ? (Glyph.Length > 1 ? 12.5f : 19f) : (Glyph.Length > 1 ? 10.5f : 14f),
                FontStyle.Bold);
            DrawCentered(g, Glyph, glyphFont, textColor,
                new RectangleF(2, 5, Width - 4, Height - (showStatus ? 28 : 12)));
        }

        if (showStatus)
        {
            using var statusFont = new Font("Segoe UI", Height >= 72 ? 7.5f : 6.5f, FontStyle.Bold);
            DrawCentered(g, Status.ToUpperInvariant(), statusFont, statusColor,
                new RectangleF(4, Height - 23, Width - 8, 17));
        }
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, RectangleF rect)
    {
        using var brush = new SolidBrush(color);
        var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        try
        {
            g.DrawString(text, font, brush, rect, format);
        }
        finally
        {
            format.Dispose();
        }
    }

    private static GraphicsPath Rounded(RectangleF r)
    {
        var path = new GraphicsPath();
        float d = 10;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
