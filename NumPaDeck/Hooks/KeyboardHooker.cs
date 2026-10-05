using System.Runtime.InteropServices;
using NumPaDeck.App;
using NumPaDeck.Native;
using NumPaDeck.Presets;

namespace NumPaDeck.Hooks;

/// <summary>
/// Installs a WH_KEYBOARD_LL hook and marshals raw key events to a target.
/// The callback runs on the UI (message pump) thread, so it must stay fast;
/// heavy work is offloaded by the router/action executor.
/// </summary>
public sealed class KeyboardHooker : IDisposable
{
    private readonly object _gate = new();

    // Strong references kept alive for the native callback. The delegate must
    // never be garbage-collected or unhooking silently breaks.
    private readonly RouterProc _router;
    private readonly NativeMethods.LowLevelKeyboardProc _proc;

    private IntPtr _hookId = IntPtr.Zero;
    private bool _installed;

    /// <summary>
    /// Callback invoked for every numpad-scoped key event on the hook thread.
    /// Parameters: (keyId, isDown, isRepeat).
    /// Returns true to swallow the event (block it from reaching the OS).
    /// </summary>
    public Func<string, bool, bool, bool>? Target { get; set; }

    public IntPtr HookId => _hookId;

    private sealed class RouterProc
    {
        public readonly KeyboardHooker Owner;
        public RouterProc(KeyboardHooker owner) => Owner = owner;

        public IntPtr OnEvent(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int msg = (int)wParam;
                bool isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;

                string? keyId = NumpadKeys.TryFromVk(info.vkCode, info.flags);
                if (keyId != null)
                {
                    var target = Owner.Target;
                    if (target != null)
                    {
                        bool swallow = false;
                        try { swallow = target(keyId, isDown, false); }
                        catch (Exception ex)
                        {
                            Log.Error("Hook target threw: " + ex.Message);
                        }
                        if (swallow) return (IntPtr)1;
                    }
                }
            }
            return NativeMethods.CallNextHookEx(Owner.HookId, nCode, wParam, lParam);
        }
    }

    public KeyboardHooker()
    {
        _router = new RouterProc(this);
        _proc = _router.OnEvent;
    }

    public void Install()
    {
        lock (_gate)
        {
            if (_installed) return;
            _hookId = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL, _proc,
                NativeMethods.GetCallingModule(), 0);
            if (_hookId == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    "Failed to install low-level keyboard hook.");
            _installed = true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_installed && _hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                _installed = false;
            }
        }
    }
}
