using System.Diagnostics;
using Staypaw.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Staypaw.Tests;

// Real steals against this desktop, so they run only when asked for: Staypaw.Tests.exe -explicit only.
// Each steal waits 11 s so it lands outside the launch grace after your last click or key.
public class StealTests
{
    // A key no program uses, so a key that goes astray does nothing.
    private const ushort TestKey = 0x97;

    // WM_APP: the steal test steals again when its thread gets it.
    private const uint StealNow = 0x8000;

    private static readonly string StealTestExe = Path.Combine(
        AppContext.BaseDirectory.Replace(Path.Combine("tests", "Staypaw.Tests"), Path.Combine("tools", "Staypaw.StealTest"), StringComparison.Ordinal),
        "Staypaw.StealTest.exe");

    // Two steal-test windows: one takes focus at 1 s and stands for your window, the other steals it at 11 s and then
    // whenever the test asks. Keys are typed only while one of them has focus, so none reach your own windows.
    [Fact(Explicit = true)]
    public unsafe void Keys_typed_during_steals_land_in_your_window_and_never_in_the_stealer()
    {
        var log = Path.Combine(Path.GetTempPath(), $"staypaw-keys-{Guid.NewGuid():N}.csv");
        using var watcher = new FocusWatcher(new FocusLog(log));
        watcher.Start();
        using var yours = StartStealTest("1 attach");
        using var thief = StartStealTest("11 attach");

        var down = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        down.Anonymous.ki.wVk = (VIRTUAL_KEY)TestKey;
        var up = down;
        up.Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        int typed = 0, typedWhileStolen = 0;
        string[] rows;

        // Raw Input never reports keys a program sends, so the watcher is told about each one: you're typing.
        void Type()
        {
            var pid = ForegroundPid();
            watcher.Input.OnKey(TestKey, down: true, Environment.TickCount);
            if ((pid == yours.Id || pid == thief.Id) && PInvoke.SendInput([down, up], sizeof(INPUT)) == 2)
            {
                typed++;
                typedWhileStolen += pid == thief.Id ? 1 : 0;
            }
        }

        try
        {
            // Your window's own arrival is a steal that stands, unless you're really typing somewhere.
            Assert.True(WaitFor(() => ForegroundPid() == yours.Id, pump: true, 10000),
                "the steal-test window never kept focus; were you typing?");

            // The stealer's first steal must find you typing to be undone. No key is sent, so none can meet it.
            var until = Environment.TickCount64 + 15000;
            while (thief.MainWindowHandle == 0 && Environment.TickCount64 < until)
            {
                watcher.Input.OnKey(TestKey, down: true, Environment.TickCount);
                Pump(TimeSpan.FromMilliseconds(10));
                thief.Refresh();
            }

            watcher.Input.OnKey(TestKey, down: true, Environment.TickCount);
            Pump(TimeSpan.FromMilliseconds(500));
            var thiefThread = PInvoke.GetWindowThreadProcessId((HWND)thief.MainWindowHandle);
            for (var steal = 0; steal < 15; steal++)
            {
                for (var i = 0; i < 5; i++)
                {
                    Type();
                    Pump(TimeSpan.FromMilliseconds(10));
                }

                // A key still unread in your window when a stealer joins its input (AttachThreadInput) and takes
                // focus goes to the stealer, or nowhere, and no hook can hold it back. So the keyboard goes quiet
                // before each steal, and only keys typed after one are tested.
                Pump(TimeSpan.FromMilliseconds(30));
                PInvoke.PostThreadMessage(thiefThread, StealNow, 0, 0);

                // Without pumping, Staypaw can't judge the steal before these keys arrive.
                if (WaitFor(() => ForegroundPid() == thief.Id, pump: false, 1000))
                {
                    Type();
                    Type();
                    Type();
                }

                WaitFor(() => ForegroundPid() == yours.Id, pump: true, 1000);

                // About 400 ms between steals, as from a stealer on a timer. Much faster, and Staypaw stops fighting
                // the app for a minute.
                Pump(TimeSpan.FromMilliseconds(250));
            }

            // A window handles WM_CLOSE ahead of keys still in its queue, so let the last held keys land first.
            Pump(TimeSpan.FromSeconds(1));
        }
        finally
        {
            Close(yours);
            Close(thief);
            rows = File.ReadAllLines(log);
            File.Delete(log);
        }

        var thiefKeys = thief.StandardOutput.ReadToEnd().Trim();
        var yoursKeys = yours.StandardOutput.ReadToEnd().Trim();
        var context = $"{typed} typed, {typedWhileStolen} while stolen; the stealer got {thiefKeys}, yours {yoursKeys}. The log:\n{string.Join('\n', rows)}";
        Assert.True(typedWhileStolen > 0, "no key was typed while the stealer had focus, so nothing was tested: " + context);
        Assert.True(thiefKeys == "keys=0", context);
        Assert.True(yoursKeys == $"keys={typed}", context);
        TestContext.Current.TestOutputHelper?.WriteLine($"{typed} keys typed, {typedWhileStolen} of them while the stealer had focus");
    }

    [Fact(Explicit = true)]
    public void A_scripted_steal_shows_up_as_a_log_row()
    {
        var rows = RunSteal(protectForeground: false, out _);

        Assert.Contains(rows, row => row.Contains(",Steal,Staypaw.StealTest.exe,", StringComparison.Ordinal));
    }

    [Fact(Explicit = true)]
    public void A_scripted_steal_from_the_protected_app_is_restored()
    {
        var rows = RunSteal(protectForeground: true, out var foregroundKept);

        Assert.Contains(rows, row => row.Contains(",Restored,Staypaw.StealTest.exe,", StringComparison.Ordinal));
        Assert.True(foregroundKept, "the protected window should have focus again after the steal");
    }

    [Fact(Explicit = true)]
    public void A_steal_from_the_protected_app_stands_while_paused_and_the_log_says_so()
    {
        var rows = RunSteal(protectForeground: true, out var foregroundKept, paused: true);

        Assert.True(rows.Any(row => row.Contains("(not restored: Staypaw is paused)", StringComparison.Ordinal)), string.Join('\n', rows));
        Assert.False(foregroundKept, "a paused Staypaw should have let the steal stand");
    }

    // Presses Ctrl+Shift+Alt+P on the real desktop. Registered, the hotkey keeps P from reaching any window.
    [Fact(Explicit = true)]
    public unsafe void The_pause_hotkey_reaches_the_watcher()
    {
        using var watcher = new FocusWatcher(new FocusLog(Path.Combine(Path.GetTempPath(), $"staypaw-hotkey-{Guid.NewGuid():N}.csv")));
        var pressed = 0;
        watcher.PauseHotkeyPressed += (_, _) => pressed++;

        VIRTUAL_KEY[] keys = [VIRTUAL_KEY.VK_CONTROL, VIRTUAL_KEY.VK_MENU, VIRTUAL_KEY.VK_SHIFT, VIRTUAL_KEY.VK_P];
        var inputs = new INPUT[keys.Length * 2];
        for (var i = 0; i < keys.Length; i++)
        {
            inputs[i].type = inputs[^(i + 1)].type = INPUT_TYPE.INPUT_KEYBOARD;
            inputs[i].Anonymous.ki.wVk = inputs[^(i + 1)].Anonymous.ki.wVk = keys[i];
            inputs[^(i + 1)].Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        }

        Assert.Equal((uint)inputs.Length, PInvoke.SendInput(inputs, sizeof(INPUT)));
        Assert.True(WaitFor(() => pressed > 0, pump: true, 2000), "no WM_HOTKEY came; does another app hold Ctrl+Shift+Alt+P?");
    }

    private static string[] RunSteal(bool protectForeground, out bool foregroundKept, bool paused = false)
    {
        var log = Path.Combine(Path.GetTempPath(), $"staypaw-steal-{Guid.NewGuid():N}.csv");
        var foreground = PInvoke.GetForegroundWindow();
        using var watcher = new FocusWatcher(new FocusLog(log));
        watcher.Start();
        watcher.ProtectedApp = protectForeground ? watcher.CurrentApp : null;
        if (paused)
        {
            watcher.Pause(TimeSpan.FromMinutes(1));
        }
        using var thief = Process.Start(new ProcessStartInfo(StealTestExe, "11 attach") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            Pump(TimeSpan.FromSeconds(13));
            foregroundKept = PInvoke.GetForegroundWindow() == foreground;
        }
        finally
        {
            thief.Kill();
        }

        var rows = File.ReadAllLines(log);
        File.Delete(log);
        return rows;
    }

    private static int ForegroundPid()
    {
        PInvoke.GetWindowThreadProcessId(PInvoke.GetForegroundWindow(), out var pid);
        return (int)pid;
    }

    private static bool WaitFor(Func<bool> done, bool pump, int timeoutMs)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (!done())
        {
            if (Environment.TickCount64 > until)
            {
                return false;
            }

            if (pump)
            {
                Pump(TimeSpan.FromMilliseconds(5));
            }
            else
            {
                Thread.Sleep(1);
            }
        }

        return true;
    }

    private static Process StartStealTest(string args) =>
        Process.Start(new ProcessStartInfo(StealTestExe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;

    // Closing its window makes the steal test print its count and exit.
    private static void Close(Process stealTest)
    {
        stealTest.Refresh();
        if (!stealTest.CloseMainWindow() || !stealTest.WaitForExit(5000))
        {
            stealTest.Kill();
        }
    }

    // The watcher's hooks and timer deliver through this thread's message queue.
    private static unsafe void Pump(TimeSpan duration)
    {
        var until = Environment.TickCount64 + (long)duration.TotalMilliseconds;
        MSG msg;
        while (Environment.TickCount64 < until)
        {
            while (PInvoke.PeekMessage(&msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                PInvoke.DispatchMessage(&msg);
            }

            Thread.Sleep(5);
        }
    }
}
