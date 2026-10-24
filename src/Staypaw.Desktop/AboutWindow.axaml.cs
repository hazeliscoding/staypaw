using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Staypaw.Desktop;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Version.Text = $"Version {typeof(AboutWindow).Assembly.GetName().Version!.ToString(3)}";
    }

    public AboutWindow(string? protectedApp) : this()
    {
        Protection.Text = protectedApp is null
            ? "To protect an app, switch to it, then right-click the paw in the tray and choose Protect."
            : $"Protecting {protectedApp}. Nothing takes focus from it unless you switch away yourself.";
    }

    private void GotIt(object? sender, RoutedEventArgs e) => Close();
}
