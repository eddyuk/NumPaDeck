# NumPaDeck

A small **Windows** utility that remaps the **numpad** to whatever you want it to do,
per saved *presets* — no external dependencies, no admin rights, single .NET 10
WinForms assembly.

NumPaDeck installs a low-level keyboard hook, swallows the numpad key presses defined
in your active preset, and performs the mapped action instead: send a key combo
(`Ctrl+Shift+T`), type a string of text, fire a media/volume key, or launch an app,
URL, or command. Keys you don't map keep behaving exactly as Windows does.

```
 7   8   9   −
 4   5   6   +
 1   2   3   =
 0   0   .   Enter
```

---

## Get it

- **winget:** `winget install eddyuk.NumPaDeck`
- **Classic installer:** download the latest `NumPaDeck-Setup-<version>.exe` from the
  [releases](https://github.com/eddyuk/NumPaDeck/releases) — per-user, no admin rights.
  The installer offers an optional *“Start NumPaDeck when you sign in”* checkbox
  (opt-in autostart via the HKCU `Run` key).
- **Portable zip:** download `NumPaDeck-<version>-win64.zip`, unzip it anywhere
  (e.g., `%LOCALAPPDATA%\Programs\NumPaDeck`) and run `NumPaDeck.exe`. The released
  builds are **self-contained** — nothing else to install.

## Requirements

- Windows 10 or 11 (x64)
- Released builds: **nothing else** — the .NET runtime is bundled in.
- Building from source: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
  The source itself has **zero NuGet dependencies**.

## Build

```powershell
dotnet build NumPaDeck\NumPaDeck.csproj -c Release
```

Output: `NumPaDeck\bin\Release\net10.0-windows\NumPaDeck.exe`

Run `NumPaDeck.exe` and an icon appears in the system tray. The first start creates a
single **Default** preset where every key passes through, so nothing about your normal
typing changes until you map something.

## Using the app

### Tray icon

Right-click the NumPaDeck tray icon for the menu:

| Item                              | What it does                                            |
|-----------------------------------|---------------------------------------------------------|
| **Presets** → *name*              | Select the active preset (checkmark marks the active one) |
| **Enable numpad hijacking**       | Global on/off. When off, every numpad key passes straight through |
| **Numpad overlay**                | Show/hide the floating numpad mirror panel (checkmark = visible) |
| **Settings…**                     | Open the settings window (double-clicking the icon works too) |
| **Exit**                          | Leave NumPaDeck running in the background and quit      |

The icon changes appearance when hijacking is disabled, and the icon's tooltip shows
the active preset name.

### Settings window

- **Left:** your presets — add, rename, duplicate, delete.
- **Right:** a visual numpad grid. Click any key to open the *key mapper* for that key
  in the selected preset. Cell tinting shows the state:
  blue = mapped, amber = gesture key, gray/dashed = "do nothing", white = passthrough.
  (Two extra cells — **÷** and **×** — sit in the row under the 4×4 grid; they cover
  the numpad keys that don't fit the standard layout.)
- **Bottom bar:** the global enable toggle, plus the **double-tap gesture** settings
  (see below).
- **Overlay row:** a *"Show numpad overlay"* checkbox and an **opacity slider
  (15–100%)** that applies live as you drag it (see below).
- **Save / Cancel:** edits apply live to the running hook; *Cancel* rolls the session
  back, *Save* (or closing the window) commits and persists.

### Key mapper dialog

For the selected numpad key, pick an **action**:

| Action          | Value to supply                                                    |
|-----------------|---------------------------------------------------------------------|
| *Do nothing*    | — the keypress is swallowed and does nothing                       |
| *Pass through*  | — keeps the normal OS behavior (the default for unmapped keys)      |
| *Key combo*     | e.g. `Ctrl+Shift+T` — use the **Capture combo…** button to record what you physically press |
| *Type string*   | the literal text to type (Unicode, any characters)                  |
| *Media*         | one of: `playpause`, `next`, `prev`, `stop`, `volumedown`, `volumeup`, `mute` |
| *Launch*        | an app path, an `http(s)://` / `mailto:` URL, or a `command args` line |

The **Test…** button performs the mapped action once so you can verify the mapping
before committing (works for key combos, media keys, and launches).

### Combo syntax

Modifiers (any of these spellings, case-insensitive): `Ctrl`/`Control`, `Alt`,
`Shift`, `Win`/`Windows`/`Cmd`/`Meta`/`Super`.

Plus **exactly one primary key**: letters, digits, `F1`–`F24`, `Space`, `Tab`, `Enter`,
`Esc`, `Home/End/PageUp/PageDown/Insert/Delete`, arrows, `; ' [ ] \ , . / - =`.
Order of modifiers in the value string doesn't matter (`Shift+Ctrl+T` ≡ `Ctrl+Shift+T`).

### Double-tap preset switching

When the gesture is enabled (default: **on**, gesture key **0**, window **250 ms**):

- **Double-tap** the gesture key (two quick presses) → switch to the *next* preset
  in the list, with a toast confirmation. Both presses are swallowed.
- **Single tap** → after the window elapses, the key performs whatever its mapping is
  (passes through with the Default preset).
- Holding the key down never counts as the second tap, and releasing the key does not
  cancel a waiting tap.

Gesture key and timing are changeable in the settings bottom bar.

### Numpad overlay

A small always-on-top panel (bottom-right of the primary screen by default) that
mirrors the current preset: the preset name up top and the 17 numpad keys as a
grid with each key's action caption (*"Next track"*, *"Ctrl+Shift+T"*, …). It's
**translucent** — the opacity is the slider in the settings overlay row — and it
stays out of the way: borderless, no taskbar entry, never steals focus.

- **Live:** switching presets (tray menu or double-tap) refreshes the panel
  instantly, and the preset name pulses briefly on change.
- **Feedback:** pressing a mapped numpad key flashes the matching cell.
- **Color language** matches the settings grid: blue = mapped, amber = gesture
  key, gray/dashed = "do nothing", dim = passthrough.
- **Draggable** from any point; it remembers where you dropped it, clamped to
  your screens.
- **Hide/show:** the × on the panel or the tray menu hides it until you
  re-enable it; the overlay is visible on first launch.
- The panel is a mirror only — the cells themselves are not clickable.

## Configuration & data

| Path                                    | What it is                                        |
|-----------------------------------------|---------------------------------------------------|
| `%APPDATA%\NumPaDeck\config.json`       | All presets, active preset, enable state, gesture config, and overlay settings (visibility, opacity, last position). Written atomically (temp file + replace). |
| `%APPDATA%\NumPaDeck\errors.log`        | Timestamped log of failures (action errors, config read/write problems, unhandled exceptions). |
| `NUMPADECK_CONFIG` env var              | Overrides the config path (used by the self-test so it never touches your real config). |

Numpad key ids used in `config.json` mappings:
`0`–`9`, `add`, `sub`, `mul`, `div`, `dot`, `enter`, `equal`.
A key that has no mapping entry behaves as *Pass through*.

## Self-verification

```powershell
# 60 offline checks: combo parser, VK mapping, media keys, SendInput layout,
# config round-trip (incl. overlay settings), and full routing/gesture
# behavior — via an injected recording sink, so NO real keystrokes, processes,
# or config files are involved.
NumPaDeck.exe --selftest

# Constructs the real settings window, key mapper, numpad overlay, and prompt
# against a throwaway config, briefly shows them, and exits 0 if the whole UI
# builds without exceptions. Writes npd_smoke_result.txt / npd_smoke_trace.txt
# to %TEMP%\opencode\.
NumPaDeck.exe --smoketest
```

## Known limitations

- **Elevated windows:** Windows UIPI prevents low-level hooks from seeing keystrokes
  typed into an *elevated* (Run as administrator) application — NumPaDeck's mapped
  actions still fire, it just can't intercept that input. Normal (un-elevated) apps
  are fully covered.
- **Single instance** is enforced per user session via the mutex
  `Local\NumPaDeck.SingleInstance`; a second start simply activates the existing
  instance. (An elevated instance would be a separate one.)
- **While settings/key-mapper dialogs are open**, numpad input passes through
  untouched — this is intentional so you can type and test combos.
- **Toast notifications** are best-effort; they only appear when a WinForms message
  pump is running on the calling thread. Failures of mapped actions are *always*
  written to `errors.log`, so nothing is silently lost.
- Key **auto-repeat**: a held-down key fires its action once; holding it does not
  re-trigger (media keys re-fire on repeat by design).

## Layout

```
NumPaDeck/
├─ Program.cs              entry point (--selftest / --smoketest / UI bootstrap)
├─ Native/NativeMethods.cs P/Invoke: SendInput, low-level keyboard hook
├─ Hooks/
│  ├─ KeyboardHooker.cs    WH_KEYBOARD_LL install/callback, numpad filtering
│  └─ PresetManager.cs     routing, gesture state machine, presets, persistence
├─ Actions/
│  ├─ ActionSinks.cs       IActionSink + RealActionSink + RecordingSink (tests)
│  ├─ InputInjector.cs     SendInput injection, media keys, launch
│  ├─ KeyComboParser.cs    "Ctrl+Shift+T" → VK sequence
│  └─ MediaAction.cs       media id → VK table
├─ Presets/                models (AppSettings, Preset, KeyMapping, Verb, …)
│  ├─ OverlayConfig.cs     overlay visibility / opacity / position model
│  └─ PresetStore.cs       atomic JSON load/save
├─ UI/                     TrayController, SettingsForm, KeyMapperDialog,
│                          NumpadCellControl, NumpadOverlayForm, PromptForm
└─ App/                    Log, Toast, IconFactory, SelfTest, SmokeTest
```
