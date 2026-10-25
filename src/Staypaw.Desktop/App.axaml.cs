using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Staypaw.Core;

namespace Staypaw.Desktop;

public partial class App : Application
{
    private static readonly string DataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Staypaw");

    private static readonly string ProtectedAppFile = Path.Combine(DataFolder, "protected-app.txt");

    // The welcome shows once, on first launch; deleting this file shows it again.
    private static readonly string WelcomedFile = Path.Combine(DataFolder, "welcomed");

    private FocusWatcher? _watcher;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private AboutWindow? _about;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Headless tests build this App without a lifetime; only the real app watches and logs.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            _watcher = FocusWatcher.CreateDefault();
            _watcher.ProtectedApp = LoadProtectedApp();
            _watcher.Changed += (_, _) => UpdateTray();
            _watcher.Start();
            UpdateTray();
            if (FirstLaunch())
            {
                StartWithWindows.Set(true);
                ShowAbout();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void UpdateTray()
    {
        var watcher = _watcher!;
        var protectedApp = watcher.ProtectedApp;
        var current = watcher.CurrentApp;
        var menu = new NativeMenu();

        menu.Items.Add(Label(protectedApp is null ? "No app protected" : $"Protecting {protectedApp}"));
        if (current is not null && !current.Equals(protectedApp, StringComparison.OrdinalIgnoreCase))
        {
            menu.Items.Add(Item($"Protect {current}", () => Protect(current)));
        }

        if (protectedApp is not null)
        {
            menu.Items.Add(Item("Stop protecting", () => Protect(null)));
        }

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Label(watcher.LastStealer is { } last ? $"Last stealer: {last}" : "No steals yet"));
        if (watcher.TopStealer is { } top)
        {
            menu.Items.Add(Label($"Top stealer: {top.App} ({top.Steals} {(top.Steals == 1 ? "steal" : "steals")})"));
        }

        var startWithWindows = StartWithWindows.IsOn();
        var startItem = Item("Start with Windows", () =>
        {
            StartWithWindows.Set(!startWithWindows);
            UpdateTray();
        });
        startItem.ToggleType = MenuItemToggleType.CheckBox;
        startItem.IsChecked = startWithWindows;
        menu.Items.Add(startItem);
        menu.Items.Add(Item("Open the focus log", OpenLog));
        menu.Items.Add(Item("About Staypaw", ShowAbout));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Quit Staypaw", () => _desktop?.Shutdown()));

        var icon = TrayIcon.GetIcons(this)![0];
        icon.Menu = menu;
        icon.ToolTipText = protectedApp is null ? "Staypaw" : $"Staypaw: protecting {protectedApp}";
    }

    private void Protect(string? app)
    {
        _watcher!.ProtectedApp = app;
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(ProtectedAppFile, app ?? "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Protection still applies until Staypaw quits; it just won't be remembered.
            Trace.WriteLine("Staypaw: couldn't save the protected app: " + ex.Message);
        }

        UpdateTray();
    }

    private static string? LoadProtectedApp()
    {
        try
        {
            var app = File.Exists(ProtectedAppFile) ? File.ReadAllText(ProtectedAppFile).Trim() : "";
            return app.Length > 0 ? app : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine("Staypaw: couldn't read the protected app: " + ex.Message);
            return null;
        }
    }

    private void ShowAbout()
    {
        if (_about is not null)
        {
            _about.Activate();
            return;
        }

        // Built on demand and dropped on close, so Calla's bitmap isn't held while Staypaw idles in the tray.
        _about = new AboutWindow(_watcher!.ProtectedApp);
        _about.Closed += (_, _) => _about = null;
        _about.Show();
    }

    private static bool FirstLaunch()
    {
        if (File.Exists(WelcomedFile))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(WelcomedFile, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The welcome still shows; it will just show again next launch.
            Trace.WriteLine("Staypaw: couldn't save the welcome marker: " + ex.Message);
        }

        return true;
    }

    private static void OpenLog()
    {
        // Before the first focus change there's no log yet, so open its folder instead.
        var target = File.Exists(FocusWatcher.LogPath) ? FocusWatcher.LogPath : Path.GetDirectoryName(FocusWatcher.LogPath)!;
        Directory.CreateDirectory(Path.GetDirectoryName(FocusWatcher.LogPath)!);
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private static NativeMenuItem Label(string text) => new(text) { IsEnabled = false };

    private static NativeMenuItem Item(string text, Action action)
    {
        var item = new NativeMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }
}
