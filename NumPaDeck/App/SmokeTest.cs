using System.Windows.Forms;
using NumPaDeck.Hooks;
using NumPaDeck.Presets;
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
            string body = verdict + " — settings window, key mapper, overlay and prompt constructed"
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

                // --- Numpad overlay: construction, live settings, grid refresh,
                // key-press flash, and position persistence ---
                var ov = new NumpadOverlayForm(mgr);
                Forms.Add(ov);

                if (!mgr.Settings.Overlay.Visible
                    || Math.Abs(mgr.Settings.Overlay.Opacity - 0.75) > 0.0001)
                    throw new Exception("overlay defaults wrong: visible="
                        + mgr.Settings.Overlay.Visible + " opacity=" + mgr.Settings.Overlay.Opacity);

                // Mapped/suppressed display: map a key, refresh, verify cell state + label.
                var p0 = mgr.Settings.Presets[0];
                mgr.SetMapping(p0.Id, "7", new KeyMapping(Verb.Media, "next"));
                mgr.SetMapping(p0.Id, "5", new KeyMapping(Verb.None, null));
                ov.RefreshFromState();
                if (ov.KindOf("7") != NumpadOverlayForm.OverlayCell.CellKind.Mapped)
                    throw new Exception("cell 7 should be Mapped, got " + ov.KindOf("7"));
                if (ov.KindOf("5") != NumpadOverlayForm.OverlayCell.CellKind.Suppressed)
                    throw new Exception("cell 5 should be Suppressed, got " + ov.KindOf("5"));
                if (!"Next track".Equals(ov.ActionOf("7"), StringComparison.OrdinalIgnoreCase))
                    throw new Exception("cell 7 action label wrong: " + ov.ActionOf("7"));
                if (ov.MappedCellCount != 1)
                    throw new Exception("MappedCellCount should be 1, got " + ov.MappedCellCount);

                // Key-press flash: an unmapped (passthrough) key executes nothing but
                // still reports activity so the mirror can flash it.
                string? seen = null;
                mgr.KeyActivity += (s, k) => seen = k;
                if (mgr.OnNumpadKey("4", true)) throw new Exception("unmapped key must pass through on down");
                if (mgr.OnNumpadKey("4", false)) throw new Exception("unmapped key must pass through on up");
                if (seen != "4") throw new Exception("KeyActivity not raised (got: " + seen + ")");
                if (ov.LastFlashedKey != "4") throw new Exception("overlay did not flash key 4");

                // Overlay settings: apply + clamp + live opacity on the form.
                mgr.SetOverlay(false, 0.4);
                if (mgr.Settings.Overlay.Visible || Math.Abs(mgr.Settings.Overlay.Opacity - 0.4) > 0.0001)
                    throw new Exception("SetOverlay did not apply");
                if (Math.Abs(ov.Opacity - 0.4) > 0.0001)
                    throw new Exception("overlay opacity not live-updated: " + ov.Opacity);
                mgr.SetOverlay(true, 5.0);
                if (!mgr.Settings.Overlay.Visible || Math.Abs(mgr.Settings.Overlay.Opacity - 1.0) > 0.0001)
                    throw new Exception("SetOverlay did not clamp to 1.0");

                // Drag/position persistence.
                ov.Location = new Point(120, 90);
                ov.PersistPosition();
                if (mgr.Settings.Overlay.X != 120 || mgr.Settings.Overlay.Y != 90)
                    throw new Exception("overlay position not persisted: "
                        + mgr.Settings.Overlay.X + "," + mgr.Settings.Overlay.Y);

                // Preset switch reaches the overlay header.
                var p2 = mgr.CreatePreset("Smoke B");
                mgr.SetActivePreset(p2.Id);
                ov.RefreshFromState();
                if (!"Smoke B".Equals(ov.HeaderName, StringComparison.Ordinal))
                    throw new Exception("overlay header not updated on preset switch: " + ov.HeaderName);
            }
            catch (Exception ex)
            {
                ctorError = ex.ToString();
            }
        }
    }
}
