using AkburaTemplateNamespace.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace AkburaTemplateNamespace.Views;

internal static class MainViewHost
{
    public static Control Create() => Create(viewModel: null);

    public static Control CreateWithDataContext(MainViewModel viewModel)
    {
        var view = Create(viewModel);
        view.DataContext = viewModel;
        return view;
    }

    private static Control Create(MainViewModel? viewModel) =>
        new PageNavigationHost
        {
            Page = new MainNavigationPage(viewModel)
        };
}

internal sealed class MainNavigationPage : NavigationPage
{
    private readonly MainViewModel? _viewModel;

    public MainNavigationPage()
        : this(viewModel: null)
    {
    }

    public MainNavigationPage(MainViewModel? viewModel)
    {
        _viewModel = viewModel;
    }

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (CurrentPage is null)
        {
            await PushAsync(new HomePage(_viewModel));
        }
    }
}

internal sealed class HomePage : ContentPage
{
    public HomePage()
        : this(viewModel: null)
    {
    }

    public HomePage(MainViewModel? viewModel)
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
            Children = { CreateMainView(viewModel), openSettings }
        };
    }

    private static MainView CreateMainView(MainViewModel? viewModel)
    {
        var view = new MainView();
        if (viewModel is not null)
        {
            view.DataContext = viewModel;
        }

        return view;
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
