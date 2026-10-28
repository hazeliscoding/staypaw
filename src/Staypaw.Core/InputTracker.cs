using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Staypaw.Core;

// Ported from NoFocusSteal's InputTracker.cs at 279e9cd (MIT); see THIRD-PARTY-NOTICES.md. Changed: switch keys
// (Alt+Tab, Alt+Esc and Win) are kept apart from other deliberate keys, and a click remembers whether it landed on
// Windows' own UI.

/// <summary>
/// Watches keyboard and mouse through Raw Input (no hooks, nothing injected into other processes) and keeps short
/// histories of when you clicked, typed or pressed a switch key. Only times are kept, never which key.
/// </summary>
internal sealed class InputTracker : IDisposable
{
    private const int HistorySize = 16;
    private const string WindowClass = "Staypaw.Input";
    private const int PauseHotkeyId = 1;

    private static readonly WNDPROC WndProcDelegate = WndProc;
    private static readonly Dictionary<nint, InputTracker> Trackers = [];
    private static bool _classRegistered;

    private readonly HWND _hwnd;
    private readonly Ring _deliberate = new(HistorySize);
    private readonly Ring _switchKeys = new(HistorySize);
    private readonly Ring _typing = new(HistorySize);
    private readonly Ring _moves = new(HistorySize);
    private readonly Ring _clicks = new(HistorySize);
    private readonly HWND[] _clickWindows = new HWND[HistorySize];
    private readonly bool[] _clickShell = new bool[HistorySize];

    // Recent raw input of any kind (thinned to one per 10 ms), to tell whether the system's last-input time is
    // input we saw or input that never reached us.
    private readonly Ring _any = new(64);
    private int _lastAny;
    private int _lastMoveRecorded;
    private bool _ctrl, _alt, _win, _altSwitched;

    public unsafe InputTracker()
    {
        if (!_classRegistered)
        {
            fixed (char* name = WindowClass)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = WndProcDelegate,
                    lpszClassName = name,
                };
                _classRegistered = PInvoke.RegisterClassEx(in wc) != 0;
            }
        }

        _hwnd = PInvoke.CreateWindowEx(0, WindowClass, "Staypaw input", 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, null, null, null);
        Trackers[_hwnd] = this;
        RAWINPUTDEVICE[] devices =
        [
            new() { usUsagePage = 1, usUsage = 2, dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK, hwndTarget = _hwnd },
            new() { usUsagePage = 1, usUsage = 6, dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK, hwndTarget = _hwnd },
        ];
        Registered = PInvoke.RegisterRawInputDevices(devices, (uint)sizeof(RAWINPUTDEVICE));

        // Ctrl+Shift+Alt+P, registered so the keyboard hook never has to watch for it. If another app holds it
        // already, the tray menu still pauses.
        PInvoke.RegisterHotKey(_hwnd, PauseHotkeyId,
            HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_ALT | HOT_KEY_MODIFIERS.MOD_SHIFT | HOT_KEY_MODIFIERS.MOD_NOREPEAT, 'P');
    }

    /// <summary>Raised on this tracker's thread when you press the pause hotkey.</summary>
    public event Action? PauseHotkey;

    public bool Registered { get; }

    /// <summary>True if Raw Input reported something close to <paramref name="time"/>.</summary>
    public bool SawInputNear(int time) => _any.Any(t => Classifier.Elapsed(t, time) is >= -150 and <= 60);

    /// <summary>
    /// Handles raw input already waiting in the queue. A focus change is judged the moment Windows reports it,
    /// and the click or keypress that caused it is usually still queued then.
    /// </summary>
    public unsafe void ProcessPending()
    {
        MSG msg;
        while (PInvoke.PeekMessage(&msg, _hwnd, PInvoke.WM_INPUT, PInvoke.WM_INPUT, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
        {
            PInvoke.DispatchMessage(&msg);
        }
    }

    public int? LastDeliberateAtOrBefore(int time) => _deliberate.LatestAtOrBefore(time);
    public int? LastSwitchKeyAtOrBefore(int time) => _switchKeys.LatestAtOrBefore(time);
    public int? LastTypingAtOrBefore(int time) => _typing.LatestAtOrBefore(time);
    public int? LastMouseMoveAtOrBefore(int time) => _moves.LatestAtOrBefore(time);

    /// <summary>Latest click at or before <paramref name="time"/> whose top-level window satisfies <paramref name="where"/>.</summary>
    public int? LastClickAtOrBefore(int time, Func<HWND, bool> where) =>
        _clicks.LatestAtOrBefore(time, i => where(_clickWindows[i]));

    public int? LastShellClickAtOrBefore(int time) => _clicks.LatestAtOrBefore(time, i => _clickShell[i]);

    public void Dispose()
    {
        PInvoke.UnregisterHotKey(_hwnd, PauseHotkeyId);
        Trackers.Remove(_hwnd);
        PInvoke.DestroyWindow(_hwnd);
    }

    private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == PInvoke.WM_INPUT && Trackers.TryGetValue(hwnd, out var tracker))
        {
            try
            {
                tracker.OnRawInput(lParam, PInvoke.GetMessageTime());
            }
            catch (Exception ex)
            {
                // A malformed input packet must never take the app down.
                System.Diagnostics.Trace.WriteLine("Staypaw: raw input failed: " + ex);
            }
        }
        else if (msg == PInvoke.WM_HOTKEY && Trackers.TryGetValue(hwnd, out var pauser))
        {
            pauser.PauseHotkey?.Invoke();
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private unsafe void OnRawInput(LPARAM handle, int time)
    {
        RAWINPUT raw;
        var size = (uint)sizeof(RAWINPUT);
        if (PInvoke.GetRawInputData(new HRAWINPUT(handle), RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, &raw, &size, (uint)sizeof(RAWINPUTHEADER)) == uint.MaxValue)
        {
            return;
        }

        if (_any.Count == 0 || Classifier.Elapsed(time, _lastAny) >= 10)
        {
            _any.Push(time);
        }

        _lastAny = time;

        // Software-generated input has no device. Faking an Alt press is the classic way to steal focus.
        if (raw.header.hDevice.IsNull)
        {
            return;
        }

        if (raw.header.dwType == (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEKEYBOARD)
        {
            OnKey(raw.data.keyboard.VKey, (raw.data.keyboard.Flags & PInvoke.RI_KEY_BREAK) == 0, time);
        }
        else if (raw.header.dwType == (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEMOUSE)
        {
            OnMouse(raw.data.mouse, time);
        }
    }

    private void OnMouse(in RAWMOUSE mouse, int time)
    {
        // Left, right, middle, X1 and X2 button-down flags.
        const ushort AnyButtonDown = 0x0001 | 0x0004 | 0x0010 | 0x0040 | 0x0100;
        if ((mouse.usButtonFlags & AnyButtonDown) != 0)
        {
            // Clicking the window you're in is working in it; clicking anything else is asking to switch.
            var clicked = HWND.Null;
            if (PInvoke.GetCursorPos(out var cursor))
            {
                var under = PInvoke.WindowFromPoint(cursor);
                if (!under.IsNull)
                {
                    clicked = PInvoke.GetAncestor(under, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);
                }
            }

            var slot = _clicks.Push(time);
            _clickWindows[slot] = clicked;
            _clickShell[slot] = !clicked.IsNull && WindowInfo.Capture(clicked).IsSystemUI;
        }

        // Movement matters when Windows activates the window under the mouse (X-Mouse). Mice report hundreds
        // of moves a second, so thin them out.
        var moved = mouse.lLastX != 0 || mouse.lLastY != 0 || ((ushort)mouse.usFlags & 0x01) != 0;
        if (moved && (_moves.Count == 0 || Classifier.Elapsed(time, _lastMoveRecorded) >= 30))
        {
            _moves.Push(time);
            _lastMoveRecorded = time;
        }
    }

    internal void OnKey(ushort vk, bool down, int time)
    {
        switch (vk)
        {
            case 0x10: // Shift on its own is neither typing nor a request to switch.
            case 0xFF: // Fake key sent as part of escaped sequences.
                return;
            case 0x11:
                _ctrl = down;
                return;
            case 0x12:
                // Alt+Tab switches when Alt is released. Alt with other keys (game hotbars, menus) isn't a switch.
                if (!down && _altSwitched)
                {
                    _switchKeys.Push(time);
                }

                _alt = down;
                _altSwitched = false;
                return;
            case 0x5B:
            case 0x5C:
                _win = down;
                _switchKeys.Push(time);
                return;
        }

        if (!down)
        {
            return;
        }

        // Checked before the stuck-modifier fix below: a wrongly counted switch only lets a change through.
        if (_alt && vk is 0x09 or 0x1B)
        {
            _switchKeys.Push(time);
            _altSwitched = true;
        }

        // A key-up can go missing (for example while a UAC prompt had the secure desktop), which would leave a
        // modifier stuck down and turn all typing into shortcuts. Trust the live key state in that case.
        _ctrl &= IsPhysicallyDown(0x11);
        _alt &= IsPhysicallyDown(0x12);
        _win &= IsPhysicallyDown(0x5B) || IsPhysicallyDown(0x5C);

        if (_ctrl || _alt || _win || IsDeliberateKey(vk))
        {
            _deliberate.Push(time);
        }
        else
        {
            _typing.Push(time);
        }
    }

    private static bool IsPhysicallyDown(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool IsDeliberateKey(ushort vk) =>
        vk is 0x09 // Tab
            or 0x0D // Enter
            or 0x1B // Esc
            or 0x5D // Menu / Apps
            or >= 0x70 and <= 0x87 // F1-F24
            or >= 0xA6 and <= 0xB7; // browser, media and app-launch keys

    /// <summary>A fixed-size history of tick-count times; the oldest entry is overwritten first.</summary>
    private sealed class Ring(int size)
    {
        private readonly int[] _times = new int[size];
        private int _next;

        public int Count { get; private set; }

        /// <summary>Records <paramref name="time"/> and returns the slot it went into.</summary>
        public int Push(int time)
        {
            var slot = _next;
            _times[slot] = time;
            _next = (_next + 1) % _times.Length;
            Count = Math.Min(Count + 1, _times.Length);
            return slot;
        }

        public bool Any(Func<int, bool> match)
        {
            for (var i = 0; i < Count; i++)
            {
                if (match(_times[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public int? LatestAtOrBefore(int time, Func<int, bool>? slotMatches = null)
        {
            int? best = null;
            for (var i = 0; i < Count; i++)
            {
                var t = _times[i];
                if (Classifier.Elapsed(time, t) < 0 || (slotMatches is not null && !slotMatches(i)))
                {
                    continue;
                }

                if (best is null || Classifier.Elapsed(t, best.Value) > 0)
                {
                    best = t;
                }
            }

            return best;
        }
    }
}
