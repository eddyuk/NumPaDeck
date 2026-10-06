using System.Drawing;
using System.Windows.Forms;
using NumPaDeck.App;
using NumPaDeck.Hooks;
using NumPaDeck.Presets;

namespace NumPaDeck.UI;

/// <summary>
/// Owns the tray icon and context menu (preset submenu, enable toggle,
/// settings, exit) and drives single-instance settings display.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly PresetManager _manager;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly ToolStripMenuItem _presetsMenu;
    private readonly Icon _iconOn;
    private readonly Icon _iconOff;
    private SettingsForm? _settingsForm;
    private NumpadOverlayForm? _overlay;
    private bool _updating;

    public TrayController(PresetManager manager)
    {
        _manager = manager;

        _iconOn = IconFactory.CreateEnabled();
        _iconOff = IconFactory.CreateDisabled();

        _presetsMenu = new ToolStripMenuItem("Presets");

        _enabledItem = new ToolStripMenuItem("Enable numpad hijacking")
        {
            CheckOnClick = true,
            Checked = _manager.Settings.Enabled
        };
        _enabledItem.CheckedChanged += (s, e) =>
        {
            if (!_updating) _manager.SetEnabled(_enabledItem.Checked);
        };

        _overlayItem = new ToolStripMenuItem("Numpad overlay")
        {
            CheckOnClick = true,
            Checked = _manager.Settings.Overlay.Visible
        };
        _overlayItem.CheckedChanged += (s, e) =>
        {
            if (!_updating)
                _manager.SetOverlay(_overlayItem.Checked, _manager.Settings.Overlay.Opacity);
        };

        var settingsItem = new ToolStripMenuItem("Settings…", null, (s, e) => ShowSettings());
        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => Application.Exit());

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_presetsMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_enabledItem);
        _menu.Items.Add(_overlayItem);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconOn,
            Text = "NumPaDeck",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (s, e) => ShowSettings();

        _manager.StateChanged += (s, e) => UpdateFromState();
        _manager.OverlayChanged += (s, e) => SyncOverlay();
        UpdateFromState();
    }

    private void UpdateFromState()
    {
        // All state changes are raised on the UI thread (hook callbacks and
        // tray events both land here), so no marshalling is needed.
        _updating = true;
        try
        {
            bool on = _manager.Settings.Enabled;
            _enabledItem.Checked = on;
            _overlayItem.Checked = _manager.Settings.Overlay.Visible;
            _notifyIcon.Icon = on ? _iconOn : _iconOff;
            _notifyIcon.Text = on
                ? "NumPaDeck — " + (_manager.ActivePreset?.Name ?? "hijacking on")
                : "NumPaDeck — passthrough";
            RebuildPresetMenu();
            SyncOverlay();
        }
        finally
        {
            _updating = false;
        }
    }

    private void RebuildPresetMenu()
    {
        _presetsMenu.DropDownItems.Clear();
        Guid? activeId = _manager.ActivePreset?.Id;
        foreach (var p in _manager.Settings.Presets)
        {
            Guid presetId = p.Id;
            var item = new ToolStripMenuItem(p.Name) { Checked = presetId == activeId };
            item.Click += (s, e) =>
            {
                _manager.SetActivePreset(presetId);
                Toast.Show("Active preset: " + p.Name);
            };
            _presetsMenu.DropDownItems.Add(item);
        }
    }

    // ------------------------------------------------------------------
    // Numpad overlay ownership (create/show/hide, never lost to GC)
    // ------------------------------------------------------------------

    private void SyncOverlay()
    {
        bool want = _manager.Settings.Overlay.Visible;
        if (want)
        {
            if (_overlay is null || _overlay.IsDisposed)
            {
                var ov = new NumpadOverlayForm(_manager);
                ov.FormClosed += (s, e) =>
                {
                    if (ReferenceEquals(_overlay, ov)) _overlay = null;
                };
                _overlay = ov;
            }
            if (!_overlay.Visible) _overlay.Show();
            _overlay.BringToFront();
        }
        else if (_overlay is { IsDisposed: false })
        {
            _overlay.Hide();
        }
    }

    public void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
                _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.BringToFront();
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_manager);
        _settingsForm.FormClosed += (s, e) => _settingsForm = null;
        _settingsForm.Show();
    }

    public void Dispose()
    {
        if (_overlay is { IsDisposed: false })
        {
            _overlay.Close();
            _overlay.Dispose();
            _overlay = null;
        }
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _iconOn.Dispose();
        _iconOff.Dispose();
    }
}
