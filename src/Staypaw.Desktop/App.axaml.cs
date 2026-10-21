using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Staypaw.Core;

namespace Staypaw.Desktop;

public partial class App : Application
{
    private FocusWatcher? _watcher;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Headless tests build this App without a lifetime; only the real app watches and logs.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            _watcher = FocusWatcher.CreateDefault();
            _watcher.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnQuit(object? sender, EventArgs e) =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
