using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Staypaw.Core;

// Ported from NoFocusSteal's FocusMonitor.cs at 279e9cd (MIT); see THIRD-PARTY-NOTICES.md. Only the watching is
// ported so far: taking focus back, flashing the stealer and backing off from apps that fight come with M2.

/// <summary>
/// Watches foreground-window changes, classifies each one and logs it. Everything happens in this process through
/// public Win32 APIs, on the thread that calls <see cref="Start"/>, which must run a message loop.
/// </summary>
public sealed class FocusWatcher : IDisposable
{
    // Input and focus events are stamped by a clock that ticks every ~16 ms, so allow that much slack when asking
    // whether an input came before a focus change.
    private const int ClockSlackMs = 20;

    // The Windows 11 input-method bug fires on every click, so log it at most once a minute.
    private const int BogusLogIntervalMs = 60000;

    private readonly FocusLog _log;
    private readonly InputTracker _input = new();
    private readonly Queue<(WindowInfo Window, int EventTime, int LastInputInfo)> _pending = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly WINEVENTPROC _onForeground;
    private readonly WINEVENTPROC _onFocus;
    private readonly TIMERPROC _onPoll;
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

    /// <summary>A watcher that logs to <c>%LOCALAPPDATA%\Staypaw\focus-log.csv</c>.</summary>
    public static FocusWatcher CreateDefault() => new(new FocusLog(FocusLog.DefaultPath));

    public void Start()
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (!foreground.IsNull)
        {
            var info = WindowInfo.Capture(foreground);
            _tracked = info.IsSystemUI ? null : info;
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

        // Judge right away. Pumping pending input can deliver more focus events re-entrantly; those join the queue.
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

        // Neither Windows' own UI nor an invisible input-method window is ever the window you were in.
        if (!next.IsSystemUI && !next.IsBogus)
        {
            _tracked = next;
        }

        var note = next.Visible ? "" : "(invisible window)";
        if (next.IsBogus)
        {
            if (_lastBogusLogged.HasValue && Classifier.Elapsed(eventTime, _lastBogusLogged.Value) < BogusLogIntervalMs)
            {
                _bogusSinceLogged++;
                return;
            }

            if (_bogusSinceLogged > 0)
            {
                note += $" (and {_bogusSinceLogged} more times in the previous minute or so)";
            }

            _lastBogusLogged = eventTime;
            _bogusSinceLogged = 0;
        }

        _log.Add(new LogEntry(DateTime.Now, Classifier.Classify(change), next, previous, note.Trim()));
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

    private static bool IsWindowOf(HWND root, WindowInfo window) =>
        !root.IsNull && (root == window.RootOwner || root == window.Hwnd);

    private static int? Latest(int? a, int? b) =>
        a is null ? b : b is null ? a : Classifier.Elapsed(a.Value, b.Value) >= 0 ? a : b;
}
