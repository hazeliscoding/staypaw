using System.Diagnostics;
using Staypaw.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Staypaw.Tests;

public class StealTests
{
    // A real steal against this desktop, so it runs only when asked for: Staypaw.Tests.exe --explicit only.
    // The steal waits 11 s so it lands outside the launch grace after your last click or key.
    [Fact(Explicit = true)]
    public void A_scripted_steal_shows_up_as_a_log_row()
    {
        var log = Path.Combine(Path.GetTempPath(), $"staypaw-steal-{Guid.NewGuid():N}.csv");
        var thiefExe = Path.Combine(
            AppContext.BaseDirectory.Replace(Path.Combine("tests", "Staypaw.Tests"), Path.Combine("tools", "Staypaw.StealTest"), StringComparison.Ordinal),
            "Staypaw.StealTest.exe");

        using var watcher = new FocusWatcher(new FocusLog(log));
        watcher.Start();
        using var thief = Process.Start(new ProcessStartInfo(thiefExe, "11 attach") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            Pump(TimeSpan.FromSeconds(13));
        }
        finally
        {
            thief.Kill();
        }

        var rows = File.ReadAllLines(log);
        File.Delete(log);
        Assert.Contains(rows, row => row.Contains(",Steal,Staypaw.StealTest.exe,", StringComparison.Ordinal));
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
