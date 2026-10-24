using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

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
}
