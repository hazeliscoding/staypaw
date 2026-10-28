using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Staypaw.Desktop;

namespace Staypaw.Tests;

public class TrayMenuTests
{
    // On Windows, Avalonia draws the tray menu with these controls. Without a theme they have no
    // template, so the menu opens empty and Quit can't be clicked.
    [Fact]
    public Task The_tray_menu_controls_have_templates() => Ui.Session.Dispatch(() =>
    {
        var app = Application.Current!;

        Assert.True(app.TryGetResource(typeof(MenuFlyoutPresenter), ThemeVariant.Default, out var presenter));
        Assert.IsType<ControlTheme>(presenter);
        Assert.True(app.TryGetResource(typeof(MenuItem), ThemeVariant.Default, out var item));
        Assert.IsType<ControlTheme>(item);
    }, TestContext.Current.CancellationToken);

    // The tray menu is drawn with MenuItems, which take an underscore as an access key and hide it, so
    // ffxiv_dx11.exe showed as ffxivdx11.exe. A doubled underscore shows as one.
    [Fact]
    public Task Exe_names_keep_their_underscores() => Ui.Session.Dispatch(() =>
    {
        var menu = ((App)Application.Current!).BuildMenu("ffxiv_dx11.exe", null, null, null, startWithWindows: false, updateAutomatically: null, pausedUntil: null);

        Assert.Equal("Protecting ffxiv__dx11.exe", ((NativeMenuItem)menu.Items[0]).Header);
    }, TestContext.Current.CancellationToken);

    // A portable copy or a build from source never updates itself, so it offers no switch for it.
    [Fact]
    public Task Only_a_copy_that_updates_itself_offers_the_switch() => Ui.Session.Dispatch(() =>
    {
        var app = (App)Application.Current!;

        Assert.DoesNotContain("Update automatically", Headers(app.BuildMenu(null, null, null, null, startWithWindows: true, updateAutomatically: null, pausedUntil: null)));
        var off = app.BuildMenu(null, null, null, null, startWithWindows: true, updateAutomatically: false, pausedUntil: null).Items
            .OfType<NativeMenuItem>().Single(item => item.Header == "Update automatically");
        Assert.Equal(MenuItemToggleType.CheckBox, off.ToggleType);
        Assert.False(off.IsChecked);
    }, TestContext.Current.CancellationToken);

    // A pause has to be seen and undone from the menu, and the menu teaches the hotkey.
    [Fact]
    public Task A_pause_shows_when_it_ends_and_offers_resume() => Ui.Session.Dispatch(() =>
    {
        var app = (App)Application.Current!;

        var running = app.BuildMenu(null, null, null, null, startWithWindows: true, updateAutomatically: null, pausedUntil: null);
        var pause = running.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Pause for 15 minutes");
        Assert.Equal(new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift), pause.Gesture);
        Assert.DoesNotContain("Resume", Headers(running));

        var paused = Headers(app.BuildMenu(null, null, null, null, startWithWindows: true, updateAutomatically: null, pausedUntil: new DateTime(2026, 10, 27, 15, 40, 0)));
        Assert.Equal(["Paused until 15:40", "Resume"], paused[..2]);
        Assert.DoesNotContain("Pause for 15 minutes", paused);
    }, TestContext.Current.CancellationToken);

    private static string?[] Headers(NativeMenu menu) => [.. menu.Items.OfType<NativeMenuItem>().Select(item => item.Header)];
}
