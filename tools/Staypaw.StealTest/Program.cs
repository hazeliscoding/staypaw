using System.Globalization;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Timer = System.Windows.Forms.Timer;

// Ported from NoFocusSteal's StealTest.cs at 279e9cd (MIT); see THIRD-PARTY-NOTICES.md. Changed: it's its own
// program, and it counts the keys that reach it.
//
// Usage: Staypaw.StealTest <delaySeconds> [alt|attach|both] [repeatMs]
// Waits, then forces its window into the foreground with the tricks misbehaving apps use. With repeatMs it keeps
// grabbing focus. It closes after 20 seconds and prints how many keys reached it.
var delaySeconds = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 3;
var method = args.Length > 1 ? args[1] : "both";
var repeatMs = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 0;
var keys = 0;

ApplicationConfiguration.Initialize();
using var form = new Form
{
    Text = "Staypaw steal test",
    ClientSize = new Size(460, 170),
    StartPosition = FormStartPosition.CenterScreen,
    KeyPreview = true,
};
form.Controls.Add(new Label
{
    Dock = DockStyle.Fill,
    Padding = new Padding(16),
    Text = "This window just took focus.\r\n\r\nIt counts the keys that reach it and closes itself in 20 seconds.",
});
form.KeyDown += (_, _) => keys++;

var delay = new Timer { Interval = Math.Max(1, delaySeconds) * 1000 };
delay.Tick += (_, _) =>
{
    delay.Stop();
    form.Show();
    Steal((HWND)form.Handle, method);
    if (repeatMs > 0)
    {
        var again = new Timer { Interval = Math.Max(100, repeatMs) };
        again.Tick += (_, _) => Steal((HWND)form.Handle, method);
        again.Start();
    }

    var close = new Timer { Interval = 20000 };
    close.Tick += (_, _) => Application.Exit();
    close.Start();
};
delay.Start();
Application.Run();
Console.WriteLine($"keys={keys}");

static void Steal(HWND hwnd, string method)
{
    if (method is "alt" or "both")
    {
        // Fake an Alt press so Windows thinks this process got the last input.
        PInvoke.keybd_event(0x12, 0, 0, 0);
        PInvoke.SetForegroundWindow(hwnd);
        PInvoke.keybd_event(0x12, 0, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, 0);
    }

    if (method is "attach" or "both")
    {
        // Borrow the foreground thread's input state, then take over.
        var foregroundThread = PInvoke.GetWindowThreadProcessId(PInvoke.GetForegroundWindow());
        var me = PInvoke.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != me && PInvoke.AttachThreadInput(me, foregroundThread, true);
        PInvoke.BringWindowToTop(hwnd);
        PInvoke.SetForegroundWindow(hwnd);
        if (attached)
        {
            PInvoke.AttachThreadInput(me, foregroundThread, false);
        }
    }
}
