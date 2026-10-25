using Avalonia;
using Avalonia.Controls;

namespace Staypaw.Desktop;

internal static class Program
{
    // Staypaw has no window, so only Quit in the tray menu ends it.
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack's Setup and uninstaller run Staypaw with --veloapp-install, --veloapp-uninstall and the like,
        // then wait up to 30 s for it to exit. Velopack's SDK would answer them, but it brings in network code.
        if (args is [var hook, ..] && hook.StartsWith("--veloapp-", StringComparison.Ordinal))
        {
            // Otherwise Windows would try to start a deleted exe at every sign-in.
            if (hook == "--veloapp-uninstall")
                StartWithWindows.Set(false);
            return 0;
        }

        // Two copies would fight over every restore, so a second launch just exits.
        using var mutex = new Mutex(true, @"Local\hazeliscoding.Staypaw", out var first);
        return first ? BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown) : 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
