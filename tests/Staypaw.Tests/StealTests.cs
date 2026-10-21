using System.Diagnostics;
using Staypaw.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Staypaw.Tests;

// Real steals against this desktop, so they run only when asked for: Staypaw.Tests.exe -explicit only.
// Each steal waits 11 s so it lands outside the launch grace after your last click or key.
public class StealTests
{
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

    private static string[] RunSteal(bool protectForeground, out bool foregroundKept)
    {
        var log = Path.Combine(Path.GetTempPath(), $"staypaw-steal-{Guid.NewGuid():N}.csv");
        var thiefExe = Path.Combine(
            AppContext.BaseDirectory.Replace(Path.Combine("tests", "Staypaw.Tests"), Path.Combine("tools", "Staypaw.StealTest"), StringComparison.Ordinal),
            "Staypaw.StealTest.exe");

        var foreground = PInvoke.GetForegroundWindow();
        using var watcher = new FocusWatcher(new FocusLog(log));
        watcher.Start();
        watcher.ProtectedApp = protectForeground ? watcher.CurrentApp : null;
        using var thief = Process.Start(new ProcessStartInfo(thiefExe, "11 attach") { UseShellExecute = false, CreateNoWindow = true })!;
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
