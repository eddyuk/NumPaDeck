using System.Windows.Forms;
using NumPaDeck.Hooks;
using NumPaDeck.UI;

namespace NumPaDeck.App;

/// <summary>
/// --smoketest: constructs and briefly shows every dialog against a
/// throwaway config to prove the UI builds without exceptions. No keyboard
/// hook is installed and no input is injected. Exits 0 on success; the
/// outcome is written to %TEMP%\opencode\npd_smoke_result.txt and any
/// phase trace to %TEMP%\opencode\npd_smoke_trace.txt.
/// </summary>
public static class SmokeTest
{
    public static int Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "opencode");
        string tmp = Path.Combine(Path.GetTempPath(), "npd_smoke_" + Guid.NewGuid().ToString("N") + ".json");
        string marker = Path.Combine(dir, "npd_smoke_trace.txt");
        string resultFile = Path.Combine(dir, "npd_smoke_result.txt");
        void Mark(string s)
        {
            try { File.AppendAllText(marker, s + Environment.NewLine); } catch { }
        }

        try { Directory.CreateDirectory(dir); } catch { }
        try { File.WriteAllText(marker, "smoketest started " + DateTime.Now.ToString("HH:mm:ss") + Environment.NewLine); } catch { }
        Environment.SetEnvironmentVariable("NUMPADECK_CONFIG", tmp);

        string? error = null;
        bool loopReturned = false;

        try
        {
            Mark("1 app init");
            Application.SetCompatibleTextRenderingDefault(false);
            Application.EnableVisualStyles();

            var ctx = new Context(out var ctorError);
            Mark("3 ctx built, ctorError=" + (ctorError == null ? "null" : ctorError.ToString()!.Split('\n')[0]));
            error ??= ctorError;

            if (error == null)
            {
                using var timer = new System.Windows.Forms.Timer { Interval = 800 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    foreach (var f in ctx.Forms)
                    {
                        try { f.Close(); }
                        catch (Exception ex) { error ??= ex.ToString(); }
                    }
                    Mark("5 close done, error=" + (error == null ? "null" : "SEEN"));
                    ctx.ExitThread();
                };
                foreach (var f in ctx.Forms) f.Show();
                Mark("6 forms shown");
                timer.Start();
                Application.Run(ctx);
                loopReturned = true;
                Mark("7 Application.Run returned");
            }
        }
        catch (Exception ex)
        {
            error ??= ex.ToString();
            Mark("8 EXCEPTION: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            Mark("9 finally");
            Environment.SetEnvironmentVariable("NUMPADECK_CONFIG", null);
            try { File.Delete(tmp); } catch { }
            string verdict = (error == null && loopReturned) ? "SMOKE OK" : "SMOKE FAIL";
            string body = verdict + " — settings window, key mapper and prompt constructed"
                + ((error == null && loopReturned) ?", shown and closed." : ".");
            if (error != null) body += "\n" + error;
            try { File.WriteAllText(resultFile, body); } catch { Mark("result write failed"); }
        }

        Console.WriteLine(error == null ? "SMOKE OK" : "SMOKE FAIL: " + error);
        return error == null && loopReturned ? 0 : 1;
    }

    private sealed class Context : ApplicationContext
    {
        public List<Form> Forms { get; } = new();

        public Context(out string? ctorError)
        {
            ctorError = null;
            try
            {
                var mgr = new PresetManager();
                Forms.Add(new SettingsForm(mgr));
                if (mgr.Settings.Presets.Count > 0)
                    Forms.Add(new KeyMapperDialog(mgr, mgr.Settings.Presets[0], "5"));
                Forms.Add(new PromptForm("Smoke", "Value?", "1"));
            }
            catch (Exception ex)
            {
                ctorError = ex.ToString();
            }
        }
    }
}
