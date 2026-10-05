using System.Runtime.InteropServices;
using System.Windows.Forms;
using NumPaDeck.App;
using NumPaDeck.Hooks;
using NumPaDeck.UI;

namespace NumPaDeck;

static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a.StartsWith("--", StringComparison.OrdinalIgnoreCase)
                          && a[2..].Equals("selftest", StringComparison.OrdinalIgnoreCase)))
        {
            return SelfTest.Run();
        }

        if (args.Any(a => a.StartsWith("--", StringComparison.OrdinalIgnoreCase)
                          && a[2..].Equals("smoketest", StringComparison.OrdinalIgnoreCase)))
        {
            return SmokeTest.Run();
        }

        try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { /* post-window: ignore */ }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (s, e) =>
            Log.Error("UI thread exception: " + e.Exception.Message);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log.Error("Unhandled exception: " + (e.ExceptionObject as Exception)?.ToString() ?? "unknown");
            if (e.IsTerminating)
                Environment.FailFast("NumPaDeck crashed — see errors.log",
                    (Exception?)e.ExceptionObject ?? new Exception("unknown"));
        };

        // Single instance per user session.
        bool createdNew;
        var mutex = new Mutex(true, @"Local\NumPaDeck.SingleInstance", out createdNew);
        if (!createdNew)
        {
            // Bring the existing instance to front if we can (settings window),
            // otherwise just point the user at the tray icon.
            IntPtr hwnd = IntPtr.Zero;
            try { hwnd = FindWindow(null, "NumPaDeck — Settings"); } catch { }
            if (hwnd != IntPtr.Zero)
            {
                try
                {
                    ShowWindow(hwnd, 9 /* SW_RESTORE */);
                    SetForegroundWindow(hwnd);
                }
                catch { }
            }
            else
            {
                MessageBox.Show("NumPaDeck is already running — look for its tray icon.",
                    "NumPaDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }

        var manager = new PresetManager();

        KeyboardHooker hooker;
        try
        {
            hooker = new KeyboardHooker();
            hooker.Target = (keyId, isDown, _) => manager.OnNumpadKey(keyId, isDown);
            hooker.Install();   // runs on this (UI) thread: callbacks are delivered here
        }
        catch (Exception ex)
        {
            Log.Error("Hook install failed: " + ex.Message);
            MessageBox.Show("NumPaDeck could not install the keyboard hook:\n\n" + ex.Message,
                "NumPaDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        using var tray = new TrayController(manager);
        using var pump = new HiddenWindow();

        Log.Info("NumPaDeck started.");
        Application.Run(pump);
        hooker.Dispose();   // remove the low-level hook before the icon/thread go away
        Log.Info("NumPaDeck exited.");

        GC.KeepAlive(mutex);
        return 0;
    }

    private sealed class HiddenWindow : Form
    {
        public HiddenWindow()
        {
            Text = "NumPaDeck";
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(1, 1);
            Opacity = 0;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
