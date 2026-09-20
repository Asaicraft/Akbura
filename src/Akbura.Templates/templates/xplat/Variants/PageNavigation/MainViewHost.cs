using AkburaTemplateNamespace.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace AkburaTemplateNamespace.Views;

internal static class MainViewHost
{
    public static Control Create() => new PageNavigationHost { Page = new MainNavigationPage() };

    public static Control CreateWithDataContext(MainViewModel viewModel)
    {
        var view = Create();
        view.DataContext = viewModel;
        return view;
    }
}

internal sealed class MainNavigationPage : NavigationPage
{
    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (CurrentPage is null)
        {
            await PushAsync(new HomePage());
        }
    }
}

internal sealed class HomePage : ContentPage
{
    public HomePage()
    {
        Header = "Home";

        var openSettings = new Button { Content = "Open Settings", Margin = new Thickness(16) };
        openSettings.Click += async (_, _) =>
        {
            if (Navigation is not null)
            {
                await Navigation.PushAsync(new SettingsPage());
            }
        };

        Content = new StackPanel
        {
            Children = { new MainView(), openSettings }
        };
    }
}

internal sealed class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        Header = "Settings";

        var back = new Button { Content = "Back to Home" };
        back.Click += async (_, _) =>
        {
            if (Navigation is not null)
            {
                await Navigation.PopAsync();
            }
        };

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Settings for your Akbura application" },
                back
            }
        };
    }
}
