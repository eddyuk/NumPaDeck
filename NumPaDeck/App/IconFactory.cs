using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace NumPaDeck.App;

/// <summary>
/// Generates the tray icon at runtime (rounded blue tile with a numpad grid),
/// enabling a gray "paused" variant without shipping .ico files.
/// </summary>
public static class IconFactory
{
    public static Icon CreateEnabled() => Build(Color.FromArgb(0, 103, 192), true);

    public static Icon CreateDisabled() => Build(Color.FromArgb(120, 124, 130), false);

    private static Icon Build(Color accent, bool enabled)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // rounded tile
            using (var path = RoundedRect(1, 1, size - 2, size - 2, 6))
            using (var fill = new SolidBrush(enabled ? Blend(accent) : Color.FromArgb(52, 58, 64)))
            {
                g.FillPath(fill, path);
            }

            // numpad dots (like the mockup: 3x3 grid + wide zero row)
            Color dots = enabled ? Color.White : Color.FromArgb(190, 196, 202);
            using (var dotBrush = new SolidBrush(dots))
            {
                int cell = 5, gap = 3, ox = 7, oy = 7;
                for (int r = 0; r < 3; r++)
                {
                    int y = oy + r * (cell + gap);
                    foreach (int c in new[] { 0, 1, 2 })
                    {
                        int x = ox + c * (cell + gap);
                        if (r == 1 && c == 0) continue;   // wide zero spans two
                        g.FillRectangle(dotBrush, x, y, cell, cell);
                    }
                }
                // wide zero in middle row
                g.FillRectangle(dotBrush, ox, oy + (cell + gap), cell * 2 + gap, cell);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static Color Blend(Color accent) => accent;

    private static GraphicsPath RoundedRect(int x, int y, int w, int h, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
