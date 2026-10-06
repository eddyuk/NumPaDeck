# NumPaDeck — Request for Proposal

A Windows utility that intercepts numpad keystrokes before they reach the OS, remaps them according to user-defined **presets**, and presents the result as a programmable "Steam-Deck-style" numpad. Runs as a taskbar (tray) icon with a settings interface for building presets.

## 1. Goals & Non-Goals

### Goals
- Intercepts any numpad keystroke before it reaches the OS/focused app.
- Per-key action mapping driven by user-built **presets** (no presets shipped; user creates them).
- Preset switching via:
  - Tray icon menu.
  - Double-tap gesture on a numpad key — **default: Numpad 0**, configurable (which key, on/off, tap window).
- Tray icon with: preset list, enable/passthrough toggle, settings, exit.
- Settings window with a visual numpad grid for per-key programming.
- Global enable/passthrough safety toggle (instant off-switch for all hijacking).

### Non-Goals (v1)
- No built-in starter presets (Media/Gaming/Dev templates).
- No auto-start with Windows.
- No global keyboard hotkeys outside the numpad gesture.
- No per-window (active application) auto-switching of presets.
- No mouse action verbs (clicks/hovers) in v1.
- No multi-monitor / multi-session support.

## 2. Architecture

### 2.1 Stack
- **Language / runtime:** C# on .NET 10 (SDK 10.0.x already installed locally).
- **UI:** WinForms. Native `NotifyIcon` tray.
- **External dependencies:** none.
- **Persistence:** JSON in `%APPDATA%\NumPaDeck\`.

### 2.2 Project layout
```
NumPaDeck.sln
NumPaDeck/
  Program.cs                     // Single-instance mutex, tray bootstrap, message loop
  Native/                        // P/Invoke: SetWindowsHookEx, SendInput, Process.Start, ShellExecute
  Hooks/KeyboardHook.cs          // WH_KEYBOARD_LL implementation
  Presets/                       // Models (Preset, Mapping, Verb), PresetManager, JSON IO
  Actions/ActionExecutor.cs      // Verb executors
  Gestures/DoubleTapDetector.cs  // Double-tap state machine
  UI/TrayController.cs           // NotifyIcon + menus
  UI/SettingsForm.cs             // Preset list + visual numpad
  UI/KeyMapperDialog.cs          // Per-key action editor + combo capture
  UI/NumpadControl.cs            // Visual numpad grid (4×4 + wide 0 / Enter)
  Config/Settings.cs             // App-level settings (gesture config, enabled state)
  Data/PresetStore.cs            // JSON load/save, atomic writes
```

### 2.3 Data model
```csharp
enum Verb { Passthrough, None, KeyCombo, TypeString, Media, Launch }

record KeyMapping(Verb Verb, string? Value); // Value shape depends on verb

record Preset(Guid Id, string Name, Dictionary<string, KeyMapping> Mappings);
// Mappings keyed by numpad key id: "0".."9", "add","sub","mul","div","dot","enter"

record AppSettings(
    bool Enabled,
    Guid ActivePresetId,
    GestureConfig Gesture);

record GestureConfig(
    bool Enabled,
    string Key,        // numpad key id, default "0"
    int TapWindowMs);  // default 250
```

Numpad keys in scope: **0–9, +, −, \*, /, ., Enter**. (Numpad Ins/Del/Ex/Dx not in v1.)

### 2.4 Runtime flow (per key event)
1. `WH_KEYBOARD_LL` callback fires (key-down or key-up, with `KBDLLHOOKSTRUCT`).
2. If the key is **not** a numpad key → pass through.
3. If the app is **disabled** (tray toggle) → pass through.
4. On key-down of the **gesture key** (default Numpad 0):
   - Two taps within `TapWindowMs` → cycle active preset → swallow both taps.
   - Single tap (window elapses) → treat as an ordinary mapping lookup for that key.
5. Else → look up `active preset → mappings[keyId]`:
   - `Passthrough` → pass through.
   - `None` → swallow both down and up.
   - `KeyCombo` / `TypeString` / `Media` / `Launch` → swallow, dispatch to `ActionExecutor` on a worker thread.
6. **Key-up decisions are paired with the key-down decision** so keys never "stick" after interception.
7. Auto-repeat events (bit 30 of `llhk.flags`) do not retrigger actions **unless** the verb is marked repeatable (`Media` is repeatable; `Launch` and `TypeString` are not).

### 2.5 Verb details
| Verb | Value format | Implementation |
|------|--------------|----------------|
| `Passthrough` | — | Do nothing; let the OS handle the key. |
| `None` | — | Swallow both edge and repeat. |
| `KeyCombo` | `"Ctrl+Shift+T"`, `"F5"` | `SendInput` / `keybd_event` sequence. |
| `TypeString` | Literal text (possibly multi-line) | `SendInput` Unicode chars; fires once per tap. |
| `Media` | `play`, `pause`, `next`, `prev`, `vol-up`, `vol-down`, `mute` | `SendInput` of `VK_MEDIA_*` / `VK_VOLUME_*` (repeatable). |
| `Launch` | App path, URL, or shell command (with confirmation guard for unknown strings that aren't paths/URLs) | `Process.Start` / `ShellExecute`; never runs while a confirmation dialog is pending. |

### 2.6 Safety & correctness
- **Low-level hook callback must stay fast** — no allocations beyond a small lock, no blocking IO; heavy work posted to a worker pool.
- **Message-pump requirement:** the hook lives on the UI thread; the WinForms message loop keeps it alive. No console-host variant.
- **Key-up pairing:** a `Dictionary<Vk, bool>` tracks keys currently being swallowed; if the corresponding key-down isn't found, the key-up still passes through to avoid orphaned states.
- **Single instance:** `Mutex("NumPaDeck.SingleInstance")`; second launch activates the existing tray icon.
- **Config durability:** JSON saved atomically (write to `.tmp` then rename). Active settings hot-reload if the file changes on disk.
- **UIPI caveat:** if the focused target app is elevated, the hook cannot intercept — documented in README, not fixable without running NumPaDeck elevated.
- **Graceful failure:** if a worker action throws, log to `%APPDATA%\NumPaDeck\errors.log` and surface a one-shot toast; never crash the hook.

## 3. UI

### 3.1 Tray icon
- **Icon states:** default (enabled) vs. muted (disabled) — two icon variants.
- **Menu (right-click):**
  - `Presets` → list of presets with checkmark on active; selecting switches live.
  - `Enable hijacking` (toggles; label + check-state reflect current).
  - `Settings…`
  - `Exit`

### 3.2 Settings window
Layout:

```
┌────────────────────────────────────────────────────────────────┐
│  [Preset list]   │   [Visual numpad grid]                     │
│                  │                                             │
│  + Preset        │     [7] [8] [9] [−]                         │
│  Rename…         │     [4] [5] [6] [+]                         │
│  Duplicate       │     [1] [2] [3] [=]                         │
│  Delete…         │     [  0  ] [.] [Enter]                     │
│                  │                                             │
├────────────────────────────────────────────────────────────────┤
│  Gesture:  [enabled] [key: Numpad 0 ▾] [tap window: 250 ms]   │
│  [Enable toggle]                     [Save]   [Cancel]         │
└────────────────────────────────────────────────────────────────┘
```

- **Left panel — preset manager:** add, rename, duplicate, delete (delete is blocked if it's the only preset or the active one without replacement).
- **Central — visual numpad:** each cell shows the key label and a small indicator of its current verb (e.g. `Ctrl+Shift+T`). Clicking a cell opens the **key mapper** for that key in the *currently active preset*.
- **Key mapper dialog:**
  - Verb dropdown (all six verbs above).
  - Value editor depending on verb:
    - `KeyCombo`: a **capture widget** — "Press the combo and release" captures the current modifiers + key via the running hook and shows it as a readable `Ctrl+Shift+T` string.
    - `TypeString`: multi-line textbox.
    - `Media`: dropdown of the 7 media actions.
    - `Launch`: path/URL box + a "test" button; unknown strings trigger a confirmation guard ("This doesn't look like a path or URL — run it anyway?").
  - `None` / `Passthrough`: value editor hidden.
- **Settings strip:** gesture key (Numpad 0 default, any of the 15 keys), tap window (100–500 ms), global enable toggle.
- **Save** commits to disk immediately; **Cancel** rolls back in-memory state only.

### 3.3 Visual numpad cell states
| State | Indication |
|-------|------------|
| Mapped to a verb | Colored border + verb label + short value text |
| `None` | Dimmed, "— " |
| `Passthrough` | Neutral, default text color |

## 4. Milestones (vertical, each testable)

1. **M1 — Hijack core.** Hook installed; per-key "swallow or pass" via a debug toggle; key-up pairing verified. *Proves the core mechanism.*
2. **M2 — Preset store.** JSON in `%APPDATA%`; add/rename/delete/duplicate presets; per-key mapping for `Passthrough`/`None`/`KeyCombo` verbs.
3. **M3 — Remaining verbs.** `TypeString`, `Media`, `Launch` (+ confirmation guard).
4. **M4 — Tray & gesture.** Tray icon with presets submenu; enable toggle; double-tap Numpad 0 cycles preset.
5. **M5 — Settings window.** Preset manager, visual numpad, key mapper with combo-capture widget, settings strip.
6. **M6 — Polish.** Single-instance mutex, error log + toast, README (install, usage, UIPI caveat), icon set.

## 5. Acceptance criteria (v1)

- [ ] `Ctrl+[numpad7]` while a preset maps `7` to `Play` sends the media Play command and the focused app does **not** receive the original `7`.
- [ ] Key holding (auto-repeat) fires the mapped action **once** for non-repeatable verbs; repeatedly for `Media`.
- [ ] Disabling the toggle passes every numpad key through unchanged (verified via a text editor typing 0–9 + operators).
- [ ] Double-tap Numpad 0 (default) cycles to the next preset with no key leaking through; single tap executes the key's mapped action.
- [ ] Restarting Windows (or killing the process) leaves no stuck keys in any app.
- [ ] A second launch of NumPaDeck activates the existing tray icon and exits.
- [ ] Deleting the active preset is prevented (or replaced by fallback) — never leaves `ActivePresetId` dangling.
- [ ] `RFP.md` and `README.md` describe install, usage, gestures, and the UIPI caveat.

## 6. Out of scope (deferred)
- Multi-monitor / per-monitor gestures.
- Per-app preset auto-switching.
- Mouse verbs (click, hover, drag).
- Auto-start with Windows.
- Preset import/export between machines.
- Theming / dark-mode UI toggles (default OS theme only).

## 7. UI mockups
Reference images live in `docs/mockups/` (editable sources in `docs/mockups/src/*.html`):

| Mockup | File | What it shows |
|--------|------|---------------|
| Numpad overlay | [`mockups/numpad-overlay.png`](mockups/numpad-overlay.png) | Translucent always-on-top live mirror of the active preset: per-key action labels, cell states, gesture key, key-press flash, drag/close annotations |
| Settings window | [`mockups/settings-window.png`](mockups/settings-window.png) | Preset manager, visual numpad with per-key verb labels, cell-state legend, gesture + enable settings strip |
| Key mapper | [`mockups/key-mapper.png`](mockups/key-mapper.png) | Per-key action editor: verb dropdown, live combo capture, current mapping, full verb list |
| Tray | [`mockups/tray-menu.png`](mockups/tray-menu.png) | Tray icon (enabled/disabled states), right-click menu, preset submenu, enable toggle |
