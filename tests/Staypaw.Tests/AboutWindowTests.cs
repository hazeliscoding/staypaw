using Avalonia.Controls;
using Avalonia.LogicalTree;
using Staypaw.Desktop;

namespace Staypaw.Tests;

public class AboutWindowTests
{
    // The window opens on first launch, so a missing sticker or broken XAML would greet a new user with a crash.
    [Fact]
    public Task The_about_window_shows_Calla_and_the_protected_app() => Ui.Session.Dispatch(() =>
    {
        var window = new AboutWindow("ffxiv_dx11.exe");
        window.Show();

        var calla = Assert.Single(window.GetLogicalDescendants().OfType<Image>());
        Assert.NotNull(calla.Source);
        Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Protecting ffxiv_dx11.exe.") == true);
        window.Close();
    }, TestContext.Current.CancellationToken);
}
