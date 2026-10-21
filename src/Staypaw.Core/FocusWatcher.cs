using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Staypaw.Core;

// Ported from NoFocusSteal's FocusMonitor.cs at 279e9cd (MIT); see THIRD-PARTY-NOTICES.md. Changed: Staypaw's own
// rules decide, focus is restored only for the protected app and the window you're typing in, and the watcher keeps
// the counts the tray menu shows.

/// <summary>
/// Watches foreground-window changes, classifies each one, puts focus back after a steal that matters, and logs it.
/// Everything happens in this process through public Win32 APIs, on the thread that calls <see cref="Start"/>, which
/// must run a message loop.
/// </summary>
public sealed class FocusWatcher : IDisposable
{
    // Input and focus events are stamped by a clock that ticks every ~16 ms, so allow that much slack when asking
    // whether an input came before a focus change.
    private const int ClockSlackMs = 20;

    // The Windows 11 input-method bug fires on every click, so log it at most once a minute.
    private const int BogusLogIntervalMs = 60000;

    // An app that takes focus back this fast is fighting us in a loop; leave it alone for a while rather than
    // ping-pong. A window that grabs focus every second or two stays restored.
    private const int FightLimit = 20;
    private const int FightWindowMs = 3000;
    private const int FightPauseMs = 60000;

    // A virtual-key code no program uses, for the fallback in ForceForeground.
    private const ushort NoOpKey = 0xE8;

    private readonly FocusLog _log;
    private readonly InputTracker _input = new();
    private readonly Queue<(WindowInfo Window, int EventTime, int LastInputInfo)> _pending = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly WINEVENTPROC _onForeground;
    private readonly WINEVENTPROC _onFocus;
    private readonly TIMERPROC _onPoll;
    private readonly Dictionary<string, int> _lastStole = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _stealCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<int>> _recentRestores = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pausedUntil = new(StringComparer.OrdinalIgnoreCase);
    private HWINEVENTHOOK _foregroundHook;
    private HWINEVENTHOOK _focusHook;
    private nuint _pollTimer;
    private HWND _lastSeenForeground;
    private WindowInfo? _tracked;
    private bool _draining;
    private int? _lastBogusLogged;
    private int _bogusSinceLogged;

    internal FocusWatcher(FocusLog log)
    {
        _log = log;
        _onForeground = (_, _, hwnd, idObject, _, _, time) =>
        {
            if (!hwnd.IsNull && idObject == 0)
            {
                Enqueue(hwnd, unchecked((int)time));
            }
        };
        _onFocus = (_, _, _, _, _, _, _) => CheckForeground();
        _onPoll = (_, _, _, _) => CheckForeground();
    }

    /// <summary>Raised after each logged focus change that wasn't to Staypaw's own window.</summary>
    public event EventHandler? Changed;

    public static string LogPath => FocusLog.DefaultPath;

    /// <summary>The exe name of the app whose windows keep focus even while you're idle, or null for none.</summary>
    public string? ProtectedApp { get; set; }

    /// <summary>The exe name of the app you were last in. Windows' own UI and Staypaw's menu don't count.</summary>
    public string? CurrentApp => _tracked?.ExeName;

    public string? LastStealer { get; private set; }

    public (string App, int Steals)? TopStealer
    {
        get
        {
            if (_stealCounts.Count == 0)
            {
                return null;
            }

            var top = _stealCounts.MaxBy(pair => pair.Value);
            return (top.Key, top.Value);
        }
    }

    /// <summary>A watcher that logs to <see cref="LogPath"/>.</summary>
    public static FocusWatcher CreateDefault() => new(new FocusLog(FocusLog.DefaultPath));

    public void Start()
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (!foreground.IsNull)
        {
            var info = WindowInfo.Capture(foreground);
            _tracked = IsTrackable(info) ? info : null;
        }

        _lastSeenForeground = foreground;
        _foregroundHook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND,
            HMODULE.Null, _onForeground, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);

        // Windows doesn't always announce a foreground change. An app that borrows your window's input connection
        // (AttachThreadInput) can take over with no EVENT_SYSTEM_FOREGROUND at all, so also check on every
        // keyboard-focus change and about 60 times a second.
        _focusHook = PInvoke.SetWinEventHook(PInvoke.EVENT_OBJECT_FOCUS, PInvoke.EVENT_OBJECT_FOCUS,
            HMODULE.Null, _onFocus, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        _pollTimer = PInvoke.SetTimer(HWND.Null, 0, 15, _onPoll);
    }

    public void Dispose()
    {
        if (_pollTimer != 0)
        {
            PInvoke.KillTimer(HWND.Null, _pollTimer);
            _pollTimer = 0;
        }

        foreach (var hook in (ReadOnlySpan<HWINEVENTHOOK>)[_foregroundHook, _focusHook])
        {
            if (!hook.IsNull)
            {
                PInvoke.UnhookWinEvent(hook);
            }
        }

        _foregroundHook = _focusHook = HWINEVENTHOOK.Null;
        _input.Dispose();
    }

    private void CheckForeground()
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull || foreground == _lastSeenForeground)
        {
            return;
        }

        _lastSeenForeground = foreground;
        if (_tracked is null || foreground != _tracked.Hwnd)
        {
            Enqueue(foreground, Environment.TickCount);
        }
    }

    private void Enqueue(HWND hwnd, int time)
    {
        try
        {
            var lastInput = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>() };
            PInvoke.GetLastInputInfo(ref lastInput);
            _pending.Enqueue((WindowInfo.Capture(hwnd), time, unchecked((int)lastInput.dwTime)));
        }
        catch (Exception ex)
        {
            Trace.WriteLine("Staypaw: capture failed: " + ex);
        }

        // Judge right away: every millisecond the stealer keeps focus is a key that may land in it. Pumping pending
        // input can deliver more focus events re-entrantly; those join the queue.
        if (_draining)
        {
            return;
        }

        _draining = true;
        try
        {
            _input.ProcessPending();
            while (_pending.TryDequeue(out var change))
            {
                try
                {
                    Evaluate(change.Window, change.EventTime, change.LastInputInfo);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("Staypaw: evaluate failed: " + ex);
                }
            }
        }
        finally
        {
            _draining = false;
        }
    }

    private void Evaluate(WindowInfo next, int eventTime, int lastInputInfo)
    {
        var previous = _tracked;
        if (previous is not null && next.Hwnd == previous.Hwnd)
        {
            return;
        }

        var until = unchecked(eventTime + ClockSlackMs);

        // Input that Raw Input didn't report (touch, pen, some accessibility tools) still moves the system-wide
        // last-input time. Compare against input seen around that moment, not the latest input: mouse moves after
        // the switch mustn't hide a click we never saw.
        int? unseen = lastInputInfo != 0 && !_input.SawInputNear(lastInputInfo) ? lastInputInfo : null;

        var change = new FocusChange
        {
            EventTime = eventTime,
            NextIsOwnProcess = next.ProcessId == _ownPid,
            NextIsSystemUI = next.IsSystemUI,
            NextIsBogus = next.IsBogus,
            HasPrevious = previous is not null,
            PreviousGone = previous is not null && WindowInfo.IsGone(previous.Hwnd),
            SameProcess = previous is not null && previous.ProcessId == next.ProcessId,
            OwnerRelated = previous is not null
                           && (next.RootOwner == previous.RootOwner || next.RootOwner == previous.Hwnd || previous.RootOwner == next.Hwnd),
            InputHiddenFromUs = previous is not null && previous.Elevated && !WindowInfo.SelfElevated,
            FollowsMouse = FollowsMouse(next, eventTime),
            LastClickOnNext = _input.LastClickAtOrBefore(until, w => IsWindowOf(w, next)),
            LastShellClick = _input.LastShellClickAtOrBefore(until),
            LastSwitchKey = _input.LastSwitchKeyAtOrBefore(until),
            LastUnseenInput = unseen,
            LastClickInPrevious = previous is null ? null : _input.LastClickAtOrBefore(until, w => IsWindowOf(w, previous)),
            LastAction = Latest(Latest(_input.LastDeliberateAtOrBefore(until), _input.LastSwitchKeyAtOrBefore(until)),
                _input.LastClickAtOrBefore(until, _ => true)),
            LastTyping = _input.LastTypingAtOrBefore(eventTime),
            NextProcessStart = next.ProcessStartTick,
        };

        var classification = Classifier.Classify(change);
        var key = BehaviorKey(next);
        var repeatStealer = _lastStole.TryGetValue(key, out var stole) && Classifier.Elapsed(eventTime, stole) <= Classifier.RepeatStealerMs;
        var note = next.Visible ? "" : "(invisible window)";
        var restored = previous is not null
                       && Classifier.ShouldRestore(classification, change, IsProtected(previous), repeatStealer)
                       && Restore(previous, next, eventTime, ref note);

        if (classification.Verdict == Verdict.Steal && !next.IsBogus)
        {
            _lastStole[key] = eventTime;
            LastStealer = next.ExeName;
            _stealCounts[next.ExeName] = _stealCounts.GetValueOrDefault(next.ExeName) + 1;
        }

        if (!restored && IsTrackable(next))
        {
            _tracked = next;
        }

        if (next.IsBogus)
        {
            if (_lastBogusLogged.HasValue && Classifier.Elapsed(eventTime, _lastBogusLogged.Value) < BogusLogIntervalMs)
            {
                _bogusSinceLogged++;
                return;
            }

            if (_bogusSinceLogged > 0)
            {
                note = Join(note, $"(and {_bogusSinceLogged} more times in the previous minute or so)");
            }

            _lastBogusLogged = eventTime;
            _bogusSinceLogged = 0;
        }

        _log.Add(new LogEntry(DateTime.Now, classification, next, previous, note, restored));
        if (!change.NextIsOwnProcess)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Puts focus back on <paramref name="previous"/>; false if Staypaw let the change stand after all.</summary>
    private bool Restore(WindowInfo previous, WindowInfo stealer, int eventTime, ref string note)
    {
        var key = BehaviorKey(stealer);

        // Bogus windows don't fight back; they get focus once per click, so never back off from them.
        if (!stealer.IsBogus && _pausedUntil.TryGetValue(key, out var pausedUntil) && Classifier.Elapsed(pausedUntil, eventTime) > 0)
        {
            note = Join(note, "(not restored: this app kept fighting back, paused for a minute)");
            return false;
        }

        var fighting = !stealer.IsBogus && CountRestore(key, eventTime);
        if (!TakeFocusBack(previous, stealer, out var alreadyBack))
        {
            if (stealer.Elevated && !WindowInfo.SelfElevated)
            {
                // Windows doesn't let a normal app take focus from an administrator one; stop trying.
                note = Join(note, "(couldn't take focus back from an administrator app)");
                return false;
            }

            // Often the switch just hasn't registered yet. Keep your window as the one you're in and look at the
            // foreground again on the next check, which retries if the stealer is still there.
            _lastSeenForeground = HWND.Null;
        }
        else if (alreadyBack && stealer.IsBogus)
        {
            note = Join(note, "(Windows gave focus back by itself; this bug can only be logged)");
            return false;
        }

        if (fighting)
        {
            note = Join(note, "(it keeps fighting back; leaving it alone for a minute)");
        }

        Flash(stealer);
        return true;
    }

    private static bool TakeFocusBack(WindowInfo previous, WindowInfo stealer, out bool alreadyBack)
    {
        var current = PInvoke.GetForegroundWindow();
        alreadyBack = current == previous.Hwnd;
        if (alreadyBack)
        {
            return true;
        }

        // Something else has come forward since; its own event will be judged on its merits.
        if (!current.IsNull && current != stealer.Hwnd && PInvoke.IsWindowVisible(current)
            && PInvoke.GetAncestor(current, GET_ANCESTOR_FLAGS.GA_ROOTOWNER) != stealer.RootOwner)
        {
            return true;
        }

        return ForceForeground(previous.Hwnd);
    }

    private static unsafe bool ForceForeground(HWND target)
    {
        var foreground = PInvoke.GetForegroundWindow();
        var foregroundThread = foreground.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(foreground);
        var me = PInvoke.GetCurrentThreadId();

        // Sharing input state with the thread that owns the foreground lets us move it.
        var attached = foregroundThread != 0 && foregroundThread != me && PInvoke.AttachThreadInput(me, foregroundThread, true);
        try
        {
            PInvoke.BringWindowToTop(target);
            PInvoke.SetForegroundWindow(target);
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(me, foregroundThread, false);
            }
        }

        if (PInvoke.GetForegroundWindow() == target)
        {
            return true;
        }

        // Fallback: Windows lets the process that produced the latest input set the foreground. Send a key no program
        // uses, then try again. It goes to the stealer, which has focus, never to the window being restored.
        var down = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        down.Anonymous.ki.wVk = (VIRTUAL_KEY)NoOpKey;
        var up = down;
        up.Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        PInvoke.SendInput([down, up], sizeof(INPUT));
        PInvoke.SetForegroundWindow(target);
        return PInvoke.GetForegroundWindow() == target;
    }

    private static unsafe void Flash(WindowInfo stealer)
    {
        var hwnd = !stealer.RootOwner.IsNull && PInvoke.IsWindowVisible(stealer.RootOwner) ? stealer.RootOwner : stealer.Hwnd;
        if (!PInvoke.IsWindowVisible(hwnd))
        {
            return;
        }

        var info = new FLASHWINFO
        {
            cbSize = (uint)sizeof(FLASHWINFO),
            hwnd = hwnd,
            dwFlags = FLASHWINFO_FLAGS.FLASHW_TRAY | FLASHWINFO_FLAGS.FLASHW_TIMERNOFG,
        };
        PInvoke.FlashWindowEx(in info);
    }

    /// <summary>Records a restore and returns true if the app now fights back so often that Staypaw backs off.</summary>
    private bool CountRestore(string key, int now)
    {
        if (!_recentRestores.TryGetValue(key, out var times))
        {
            _recentRestores[key] = times = [];
        }

        times.RemoveAll(t => Classifier.Elapsed(now, t) > FightWindowMs);
        times.Add(now);
        if (times.Count < FightLimit)
        {
            return false;
        }

        _pausedUntil[key] = unchecked(now + FightPauseMs);
        times.Clear();
        return true;
    }

    /// <summary>With X-Mouse on, pointing at a window is how you switch to it: the pointer moved shortly before
    /// and is over the new window.</summary>
    private unsafe bool FollowsMouse(WindowInfo next, int eventTime)
    {
        BOOL tracking;
        if (!PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETACTIVEWINDOWTRACKING, 0, &tracking, 0) || !tracking)
        {
            return false;
        }

        uint delay;
        PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETACTIVEWNDTRKTIMEOUT, 0, &delay, 0);
        var lastMove = _input.LastMouseMoveAtOrBefore(eventTime);
        if (lastMove is null || Classifier.Elapsed(eventTime, lastMove.Value) > (int)Math.Min(delay, 10000) + 1000
            || !PInvoke.GetCursorPos(out var cursor))
        {
            return false;
        }

        var under = PInvoke.WindowFromPoint(cursor);
        return !under.IsNull && IsWindowOf(PInvoke.GetAncestor(under, GET_ANCESTOR_FLAGS.GA_ROOTOWNER), next);
    }

    private bool IsProtected(WindowInfo window) =>
        ProtectedApp is not null && window.ExeName.Equals(ProtectedApp, StringComparison.OrdinalIgnoreCase);

    // Windows' own UI, an invisible input-method window and Staypaw's menu are never the window you were in.
    private bool IsTrackable(WindowInfo window) => !window.IsSystemUI && !window.IsBogus && window.ProcessId != _ownPid;

    /// <summary>
    /// What "the same stealer" means: the program plus its window class. Host processes such as explorer.exe own
    /// unrelated windows (desktop, folders, copy dialogs), and one misbehaving shouldn't taint the rest.
    /// </summary>
    private static string BehaviorKey(WindowInfo window) => window.ExeName + "|" + window.ClassName;

    private static bool IsWindowOf(HWND root, WindowInfo window) =>
        !root.IsNull && (root == window.RootOwner || root == window.Hwnd);

    private static int? Latest(int? a, int? b) =>
        a is null ? b : b is null ? a : Classifier.Elapsed(a.Value, b.Value) >= 0 ? a : b;

    private static string Join(string a, string b) => a.Length == 0 ? b : a + " " + b;
}
