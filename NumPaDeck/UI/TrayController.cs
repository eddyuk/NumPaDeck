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
    private readonly ToolStripMenuItem _presetsMenu;
    private readonly Icon _iconOn;
    private readonly Icon _iconOff;
    private SettingsForm? _settingsForm;
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

        var settingsItem = new ToolStripMenuItem("Settings…", null, (s, e) => ShowSettings());
        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => Application.Exit());

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_presetsMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_enabledItem);
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
            _notifyIcon.Icon = on ? _iconOn : _iconOff;
            _notifyIcon.Text = on
                ? "NumPaDeck — " + (_manager.ActivePreset?.Name ?? "hijacking on")
                : "NumPaDeck — passthrough";
            RebuildPresetMenu();
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
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _iconOn.Dispose();
        _iconOff.Dispose();
    }
}
