using System.Drawing;
using System.Windows.Forms;

namespace NumPaDeck.App;

/// <summary>
/// Small auto-dismissing toast in the top-right corner for lightweight
/// feedback (preset switches, action errors). Best-effort: silently does
/// nothing when no WinForms message loop exists (e.g. --selftest).
/// </summary>
public static class Toast
{
    public static void Show(string message)
    {
        // Only a thread that owns a WinForms message loop can host the toast.
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            return;

        try
        {
            ShowOnUIThread(message);
        }
        catch
        {
            // best-effort feedback only
        }
    }

    private static void ShowOnUIThread(string message)
    {
        try
        {
            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = Color.FromArgb(32, 33, 36)
            };

            var label = new Label
            {
                Text = message,
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10f),
                Padding = new Padding(14, 0, 14, 0)
            };
            form.Controls.Add(label);
            form.Font = label.Font;

            int w = Math.Max(210, label.PreferredWidth + 28);
            form.Size = new Size(w, 44);

            if (Screen.PrimaryScreen != null)
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                form.Location = new Point(wa.Right - w - 14, wa.Top + 14);
            }
            else
            {
                form.Location = new Point(100, 100);
            }

            var timer = new System.Windows.Forms.Timer { Interval = 2200 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                form.Close();
            };
            timer.Start();
            form.Show();
        }
        catch
        {
            // best-effort feedback only
        }
    }
}
