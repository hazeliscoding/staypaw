using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Staypaw.Desktop;

// Renders the welcome window and the tray menu from made-up state, in light and dark, for the README. Made-up
// state keeps the owner's apps and log out of the pictures, and the same pictures come out every time.
// Usage: dotnet run --project tools/Staypaw.Screenshots -- docs/screenshots
var output = Directory.CreateDirectory(args is [var folder] ? folder : Path.Combine("artifacts", "screenshots"));

using var session = HeadlessUnitTestSession.StartNew(typeof(Shots));

// Waited for here rather than awaited: an await would carry on on the session's own thread, which then can't stop itself.
session.Dispatch(() =>
{
    // Each border is the theme's Border1 token from Tokens.axaml.
    foreach (var (theme, border, name) in new[]
    {
        (ThemeVariant.Dark, Color.Parse("#3A342C"), "dark"),
        (ThemeVariant.Light, Color.Parse("#D6CDBB"), "light"),
    })
    {
        Application.Current!.RequestedThemeVariant = theme;

        var welcome = new AboutWindow("ffxiv_dx11.exe");
        welcome.Show();
        Save(welcome, border, Path.Combine(output.FullName, $"welcome-{name}.png"));
        welcome.Close();

        var menu = ((App)Application.Current).BuildMenu("ffxiv_dx11.exe", "ffxiv_dx11.exe", "setup.exe", ("chrome.exe", 12), startWithWindows: true, updateAutomatically: true, pausedUntil: null);
        var tray = new Window
        {
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = null,
            Content = new MenuFlyoutPresenter
            {
                ItemsSource = menu.Items.Select(ToControl).ToList(),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            },
        };
        tray.Show();
        // Show lays the window out at its default size; sizing it to the menu takes another pass, and widening it for
        // the hotkey's column a third, once the dispatcher has run.
        tray.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        tray.UpdateLayout();
        Save(tray, null, Path.Combine(output.FullName, $"tray-{name}.png"));
        tray.Close();
    }
}, CancellationToken.None).GetAwaiter().GetResult();

// As Avalonia's Windows tray turns a NativeMenu into the controls of its popup.
static Control ToControl(NativeMenuItemBase item) => item is NativeMenuItem native
    ? new MenuItem { Header = native.Header, IsEnabled = native.IsEnabled, ToggleType = native.ToggleType, IsChecked = native.IsChecked, InputGesture = native.Gesture }
    : new Separator();

// At twice the pixels, for sharp text on high-DPI screens. A window gets rounded corners and a hairline border,
// since headless windows have no frame; the menu draws its own.
static void Save(Window window, Color? border, string path)
{
    var size = window.ClientSize;
    var pixels = new PixelSize((int)size.Width * 2, (int)size.Height * 2);
    var dpi = new Vector(192, 192);
    using var frame = new RenderTargetBitmap(pixels, dpi);
    frame.Render(window);

    using var framed = new RenderTargetBitmap(pixels, dpi);
    using (var context = framed.CreateDrawingContext())
    {
        var outline = new RoundedRect(new Rect(size), border is null ? 0 : 8);
        using (context.PushClip(outline))
        {
            // The source rectangle is in the frame's pixels, the destination in layout units.
            context.DrawImage(frame, new Rect(0, 0, pixels.Width, pixels.Height), new Rect(size));
        }

        if (border is { } color)
        {
            context.DrawRectangle(null, new Pen(new SolidColorBrush(color), 1), outline.Deflate(0.5, 0.5));
        }
    }

    framed.Save(path, new PngBitmapEncoderOptions());
    Console.WriteLine($"Wrote {path}");
}

internal static class Shots
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
