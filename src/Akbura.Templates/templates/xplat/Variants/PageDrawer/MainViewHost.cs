using AkburaTemplateNamespace.ViewModels;
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

    private static Control Create(MainViewModel? viewModel)
    {
        var menu = new ListBox
        {
            ItemsSource = new[] { "Home", "Settings" },
            SelectedIndex = 0
        };

        var drawer = new DrawerPage
        {
            Header = "Akbura",
            Drawer = menu,
            Content = CreatePage(0, viewModel)
        };

        menu.SelectionChanged += (_, _) =>
        {
            if (menu.SelectedIndex < 0)
            {
                return;
            }

            drawer.Content = CreatePage(menu.SelectedIndex, viewModel);
            drawer.IsOpen = false;
        };

        return new PageNavigationHost { Page = drawer };
    }

    private static ContentPage CreatePage(
        int selectedIndex,
        MainViewModel? viewModel) => selectedIndex switch
    {
        0 => new ContentPage
        {
            Header = "Home",
            Content = CreateMainView(viewModel)
        },
        1 => new ContentPage
        {
            Header = "Settings",
            Content = new TextBlock { Text = "Settings for your Akbura application", Margin = new Avalonia.Thickness(24) }
        },
        _ => throw new ArgumentOutOfRangeException(nameof(selectedIndex))
    };

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
