using Avalonia;
using Avalonia.Controls;
using Velopack;

namespace Staypaw.Desktop;

internal static class Program
{
    // Staypaw has no window, so only Quit in the tray menu ends it.
    [STAThread]
    public static int Main(string[] args)
    {
        // Setup and the uninstaller start an installed copy with their own arguments; Velopack handles those and
        // exits. It also installs an update that was downloaded before the last exit.
        if (Updates.IsInstalled)
        {
            VelopackApp.Build()
                // Otherwise Windows would try to start a deleted exe at every sign-in.
                .OnBeforeUninstallFastCallback(_ => StartWithWindows.Set(false))
                .Run();
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
