using Avalonia;
using Avalonia.Controls;

namespace Staypaw.Desktop;

internal static class Program
{
    // Staypaw has no window, so only Quit in the tray menu ends it.
    [STAThread]
    public static int Main(string[] args)
    {
        // Two copies would fight over every restore, so a second launch just exits.
        using var mutex = new Mutex(true, @"Local\hazeliscoding.Staypaw", out var first);
        return first ? BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown) : 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
