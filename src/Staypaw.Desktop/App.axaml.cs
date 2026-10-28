using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Oopsling.Avalonia;
using Staypaw.Core;

namespace Staypaw.Desktop;

public partial class App : Application
{
    private static readonly string DataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Staypaw");

    private static readonly string ProtectedAppFile = Path.Combine(DataFolder, "protected-app.txt");

    // The welcome shows once, on first launch; deleting this file shows it again.
    private static readonly string WelcomedFile = Path.Combine(DataFolder, "welcomed");

    // Updates are on unless this file exists.
    private static readonly string UpdatesOffFile = Path.Combine(DataFolder, "updates-off");

    // Every pause ends by itself, so a forgotten one can't leave you unprotected all day. It's never saved, so a
    // restart comes back protected.
    private static readonly TimeSpan PauseLength = TimeSpan.FromMinutes(15);

    private FocusWatcher? _watcher;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private AboutWindow? _about;
    private DateTime _pauseEnds;
    private IDisposable? _pauseTimer;

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
            _watcher.PauseHotkeyPressed += (_, _) => SetPaused(!_watcher.Paused);
            _watcher.Start();
            UpdateTray();
            if (Updates.IsInstalled)
            {
                StartUpdates(desktop);
            }

            if (FirstLaunch())
            {
                StartWithWindows.Set(true);
                ShowAbout();
            }

            // Staypaw has no main window, so the last crash's report shows on its own.
            CrashReportWindow.ShowUnshown();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void UpdateTray()
    {
        var watcher = _watcher!;
        var icon = TrayIcon.GetIcons(this)![0];
        DateTime? pausedUntil = watcher.Paused ? _pauseEnds : null;
        icon.Menu = BuildMenu(watcher.ProtectedApp, watcher.CurrentApp, watcher.LastStealer, watcher.TopStealer, StartWithWindows.IsOn(),
            Updates.IsInstalled ? UpdateAutomatically() : null, pausedUntil);
        icon.ToolTipText = pausedUntil is { } until ? $"Staypaw: paused until {until:HH:mm}"
            : watcher.ProtectedApp is null ? "Staypaw" : $"Staypaw: protecting {watcher.ProtectedApp}";
    }

    // Takes the watcher's state as arguments, so the screenshot tool builds this same menu from made-up state.
    // updateAutomatically is null for a copy that can't update itself, and pausedUntil is null unless paused.
    public NativeMenu BuildMenu(string? protectedApp, string? current, string? lastStealer, (string App, int Steals)? topStealer, bool startWithWindows,
        bool? updateAutomatically, DateTime? pausedUntil)
    {
        var menu = new NativeMenu();

        var hotkey = new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);
        if (pausedUntil is { } until)
        {
            menu.Items.Add(Label($"Paused until {until:HH:mm}"));
            menu.Items.Add(Item("Resume", () => SetPaused(false), hotkey));
            menu.Items.Add(new NativeMenuItemSeparator());
        }

        menu.Items.Add(Label(protectedApp is null ? "No app protected" : $"Protecting {protectedApp}"));
        if (current is not null && !current.Equals(protectedApp, StringComparison.OrdinalIgnoreCase))
        {
            menu.Items.Add(Item($"Protect {current}", () => Protect(current)));
        }

        if (protectedApp is not null)
        {
            menu.Items.Add(Item("Stop protecting", () => Protect(null)));
        }

        if (pausedUntil is null)
        {
            menu.Items.Add(Item("Pause for 15 minutes", () => SetPaused(true), hotkey));
        }

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Label(lastStealer is { } last ? $"Last stealer: {last}" : "No steals yet"));
        if (topStealer is { } top)
        {
            menu.Items.Add(Label($"Top stealer: {top.App} ({top.Steals} {(top.Steals == 1 ? "steal" : "steals")})"));
        }

        menu.Items.Add(Toggle("Start with Windows", startWithWindows, () =>
        {
            StartWithWindows.Set(!startWithWindows);
            UpdateTray();
        }));
        if (updateAutomatically is { } update)
        {
            menu.Items.Add(Toggle("Update automatically", update, () =>
            {
                SetUpdateAutomatically(!update);
                UpdateTray();
            }));
        }

        menu.Items.Add(Item("Open the focus log", OpenLog));
        menu.Items.Add(Item("About Staypaw", ShowAbout));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Quit Staypaw", () => _desktop?.Shutdown()));
        return menu;
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

    private void SetPaused(bool paused)
    {
        _pauseTimer?.Dispose();
        _pauseTimer = paused ? DispatcherTimer.RunOnce(() => SetPaused(false), PauseLength) : null;
        _pauseEnds = DateTime.Now + PauseLength;
        _watcher!.Pause(paused ? PauseLength : TimeSpan.Zero);
        using (var ico = AssetLoader.Open(new Uri(paused ? "avares://Staypaw/Assets/staypaw-paused.ico" : "avares://Staypaw/Assets/staypaw.ico")))
        {
            TrayIcon.GetIcons(this)![0].Icon = new WindowIcon(ico);
        }

        UpdateTray();
    }

    private void StartUpdates(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var watcher = _watcher!;
        var updates = new Updates(UpdateAutomatically, SynchronizationContext.Current!);
        var gate = new UpdateGate(
            () => watcher.ProtectedApp is { } app && app.Equals(watcher.CurrentApp, StringComparison.OrdinalIgnoreCase),
            () =>
            {
                try
                {
                    updates.InstallAfterExit();
                }
                catch (Exception ex)
                {
                    // The updater didn't start: keep running, and the update installs at the next start.
                    Trace.WriteLine("Staypaw: couldn't start the updater: " + ex.Message);
                    return;
                }

                desktop.Shutdown();
            });
        watcher.Changed += (_, _) => gate.FocusChanged();
        updates.Downloaded += gate.Downloaded;
        desktop.Exit += (_, _) => updates.Dispose();
        updates.Start();
    }

    private static bool UpdateAutomatically() => !File.Exists(UpdatesOffFile);

    private static void SetUpdateAutomatically(bool on)
    {
        try
        {
            if (on)
            {
                File.Delete(UpdatesOffFile);
            }
            else
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllText(UpdatesOffFile, "");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine("Staypaw: couldn't change automatic updates: " + ex.Message);
        }
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

    private static NativeMenuItem Label(string text) => new(Literal(text)) { IsEnabled = false };

    private static NativeMenuItem Item(string text, Action action, KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(Literal(text)) { Gesture = gesture };
        item.Click += (_, _) => action();
        return item;
    }

    private static NativeMenuItem Toggle(string text, bool on, Action action)
    {
        var item = Item(text, action);
        item.ToggleType = MenuItemToggleType.CheckBox;
        item.IsChecked = on;
        return item;
    }

    // A menu item reads _ as an access key, so ffxiv_dx11.exe showed as ffxivdx11.exe.
    private static string Literal(string text) => text.Replace("_", "__", StringComparison.Ordinal);
}
