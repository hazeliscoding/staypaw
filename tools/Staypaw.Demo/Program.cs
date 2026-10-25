using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

// Records the README's demo: the same typing and the same steals, first without Staypaw, then with it. It runs the
// real Staypaw and the steal test on the real desktop, so don't type or move the mouse for about 30 seconds. Only
// its own window is recorded, with the steal test's window showing through a hole in it.
// Needs ffmpeg on PATH. Usage: dotnet run --project tools/Staypaw.Demo -- <Staypaw.exe> docs/screenshots/demo.gif
if (args is not [var staypawExe, var gif])
{
    Console.Error.WriteLine("Usage: Staypaw.Demo <Staypaw.exe> <out.gif>");
    return 2;
}

if (Process.GetProcessesByName("Staypaw").Length > 0)
{
    Console.Error.WriteLine("Quit Staypaw first: a second copy exits at once.");
    return 1;
}

const string Sentence = "Notes for Friday: ship 1.0, write the release notes, and tell everyone.";
int[] stealsAt = [12, 28, 44, 58];
var paper = ColorTranslator.FromHtml("#F2EDE2");
var ink = ColorTranslator.FromHtml("#1D1A16");

var stealTestExe = Path.Combine(
    AppContext.BaseDirectory.Replace(Path.Combine("tools", "Staypaw.Demo"), Path.Combine("tools", "Staypaw.StealTest"), StringComparison.Ordinal),
    "Staypaw.StealTest.exe");
var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Staypaw");
var protectedFile = Path.Combine(data, "protected-app.txt");
var welcomedFile = Path.Combine(data, "welcomed");
var savedProtected = File.Exists(protectedFile) ? File.ReadAllText(protectedFile) : null;
var wasWelcomed = File.Exists(welcomedFile);
var clips = Directory.CreateTempSubdirectory("staypaw-demo-");

ApplicationConfiguration.Initialize();
try
{
    // Staypaw protects this window, as you'd protect your editor, and skips its welcome.
    Directory.CreateDirectory(data);
    File.WriteAllText(protectedFile, Path.GetFileName(Environment.ProcessPath!));
    File.WriteAllText(welcomedFile, "");

    var without = Record(null, "Without Staypaw, the window that took focus gets your keys.", Path.Combine(clips.FullName, "without.mkv"));
    var with = Record(staypawExe, "With Staypaw, focus comes straight back, and your typing stays put.", Path.Combine(clips.FullName, "with.mkv"));

    using var encode = Ffmpeg($"-i \"{without}\" -i \"{with}\" -filter_complex \"[0][1]concat=n=2:v=1[v];[v]fps=12,scale=880:-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle\" \"{gif}\"");
    encode.WaitForExit();
    if (encode.ExitCode != 0)
    {
        throw new InvalidOperationException($"ffmpeg exited with {encode.ExitCode}");
    }

    Console.WriteLine($"Wrote {Path.GetFullPath(gif)}");
    return 0;
}
finally
{
    // Put back the owner's own settings.
    if (savedProtected is null)
    {
        File.Delete(protectedFile);
    }
    else
    {
        File.WriteAllText(protectedFile, savedProtected);
    }

    if (!wasWelcomed)
    {
        File.Delete(welcomedFile);
    }

    clips.Delete(recursive: true);
}

string Record(string? staypaw, string caption, string clip)
{
    using var guard = staypaw is null ? null : Process.Start(Path.GetFullPath(staypaw));
    if (guard is not null)
    {
        // Time to set its hooks before anything takes focus.
        Thread.Sleep(1500);
    }

    using var thief = Process.Start(new ProcessStartInfo(stealTestExe, "1 both") { UseShellExecute = false })!;
    var until = Environment.TickCount64 + 10000;
    while (thief.MainWindowHandle == 0 && Environment.TickCount64 < until)
    {
        Thread.Sleep(50);
        thief.Refresh();
    }

    var thiefWindow = (HWND)thief.MainWindowHandle;
    var thiefFrame = Frame(thiefWindow);
    var thiefWidth = thiefFrame.right - thiefFrame.left;
    var thiefHeight = thiefFrame.bottom - thiefFrame.top;

    using var form = new Form
    {
        FormBorderStyle = FormBorderStyle.None,
        StartPosition = FormStartPosition.Manual,
        AutoScaleMode = AutoScaleMode.None,
        BackColor = paper,
        Text = "Staypaw demo",
    };
    var scale = form.DeviceDpi / 96f;
    int Px(int value) => (int)Math.Round(value * scale);
    var margin = Px(24);
    var captionHeight = Px(56);
    var labelHeight = Px(28);
    var editorWidth = Px(520);
    var width = margin + editorWidth + margin + thiefWidth + margin;
    var height = captionHeight + labelHeight + thiefHeight + margin;
    var area = Screen.PrimaryScreen!.WorkingArea;
    // Even sizes, which the encoders need.
    form.Bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width + width % 2, height + height % 2);

    var hole = new Rectangle(margin + editorWidth + margin, captionHeight + labelHeight, thiefWidth, thiefHeight);
    var editor = new TextBox
    {
        Bounds = new Rectangle(margin, hole.Top, editorWidth, thiefHeight),
        Multiline = true,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = ColorTranslator.FromHtml("#FBF8F2"),
        ForeColor = ink,
        Font = new Font("Segoe UI", 13),
    };
    form.Controls.Add(new Label { Bounds = new Rectangle(margin, 0, width - 2 * margin, captionHeight), Text = caption, TextAlign = ContentAlignment.MiddleLeft, ForeColor = ink, Font = new Font("Segoe UI Semibold", 14) });
    form.Controls.Add(new Label { Bounds = new Rectangle(margin, captionHeight, editorWidth, labelHeight), Text = "Your window", ForeColor = ink, Font = new Font("Segoe UI", 10) });
    form.Controls.Add(new Label { Bounds = new Rectangle(hole.Left, captionHeight, thiefWidth, labelHeight), Text = "An app that takes focus", ForeColor = ink, Font = new Font("Segoe UI", 10) });
    form.Controls.Add(editor);

    // The steal test's window is another app's, so it sits in a hole rather than on top: whichever window is in
    // front, both show. The hole is rounded like the window and covers its see-through border, so nothing behind
    // shows at the edges.
    var region = new Region(new Rectangle(Point.Empty, form.Size));
    using var rounded = new GraphicsPath();
    rounded.AddRoundedRectangle(Rectangle.Inflate(hole, -1, -1), new Size(Px(16), Px(16)));
    region.Exclude(rounded);
    form.Region = region;
    PInvoke.GetWindowRect(thiefWindow, out var thiefRect);
    PInvoke.SetWindowPos(thiefWindow, HWND.Null,
        form.Left + hole.Left - (thiefFrame.left - thiefRect.left), form.Top + hole.Top - (thiefFrame.top - thiefRect.top), 0, 0,
        SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    form.Shown += async (_, _) =>
    {
        // Take the foreground back from the steal test the way Staypaw does: the process that sent the latest input
        // may. 0xE8 is a key no program uses, and the steal test doesn't count it, as it would Alt.
        PInvoke.keybd_event(0xE8, 0, 0, 0);
        PInvoke.keybd_event(0xE8, 0, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, 0);
        PInvoke.SetForegroundWindow((HWND)form.Handle);
        editor.Focus();
        await Task.Delay(500);

        using var recording = Ffmpeg(
            $"-f gdigrab -framerate 30 -draw_mouse 0 -offset_x {form.Left} -offset_y {form.Top} -video_size {form.Width}x{form.Height} -i desktop -c:v libx264rgb -crf 0 -preset ultrafast \"{clip}\"",
            input: true);
        await Task.Delay(1000);

        var thiefThread = PInvoke.GetWindowThreadProcessId(thiefWindow);
        for (var i = 0; i < Sentence.Length; i++)
        {
            if (stealsAt.Contains(i))
            {
                // As in the key test: a key your window hasn't read yet when a stealer joins its input goes with it,
                // and no hook can hold it back. So the typing pauses before each steal.
                await Task.Delay(40);
                PInvoke.PostThreadMessage(thiefThread, 0x8000, 0, 0);

                // Keys typed before the restore are held, and held keys aimed at the protected app are dropped, as for
                // a game. So the typing waits for focus to come back, or for good without Staypaw.
                var until = Environment.TickCount64 + 300;
                await Task.Delay(40);
                while (PInvoke.GetForegroundWindow() != (HWND)form.Handle && Environment.TickCount64 < until)
                {
                    await Task.Delay(10);
                }

                await Task.Delay(40);
            }

            Type(Sentence[i]);
            await Task.Delay(70);
        }

        await Task.Delay(1500);
        recording.StandardInput.Write('q');
        recording.StandardInput.Close();
        await recording.WaitForExitAsync();
        form.Close();
    };
    Application.Run(form);

    thief.Kill();
    guard?.Kill();
    return clip;
}

static Process Ffmpeg(string arguments, bool input = false) =>
    Process.Start(new ProcessStartInfo("ffmpeg", "-y -hide_banner -loglevel error " + arguments) { UseShellExecute = false, RedirectStandardInput = input })!;

static unsafe RECT Frame(HWND window)
{
    // The visible frame, without the invisible resize borders that GetWindowRect counts.
    RECT frame;
    PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &frame, (uint)sizeof(RECT)).ThrowOnFailure();
    return frame;
}

static void Type(char c)
{
    var down = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
    down.Anonymous.ki.wScan = c;
    down.Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE;
    var up = down;
    up.Anonymous.ki.dwFlags |= KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
    PInvoke.SendInput([down, up], Marshal.SizeOf<INPUT>());
}
