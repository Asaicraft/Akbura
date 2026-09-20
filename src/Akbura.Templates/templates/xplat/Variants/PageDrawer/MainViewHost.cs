using AkburaTemplateNamespace.ViewModels;
using Avalonia.Controls;

namespace AkburaTemplateNamespace.Views;

internal static class MainViewHost
{
    public static Control Create()
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
            Content = CreatePage(0)
        };

        menu.SelectionChanged += (_, _) =>
        {
            if (menu.SelectedIndex < 0)
            {
                return;
            }

            drawer.Content = CreatePage(menu.SelectedIndex);
            drawer.IsOpen = false;
        };

        return new PageNavigationHost { Page = drawer };
    }

    public static Control CreateWithDataContext(MainViewModel viewModel)
    {
        var view = Create();
        view.DataContext = viewModel;
        return view;
    }

    private static ContentPage CreatePage(int selectedIndex) => selectedIndex switch
    {
        0 => new ContentPage { Header = "Home", Content = new MainView() },
        1 => new ContentPage
        {
            Header = "Settings",
            Content = new TextBlock { Text = "Settings for your Akbura application", Margin = new Avalonia.Thickness(24) }
        },
        _ => throw new ArgumentOutOfRangeException(nameof(selectedIndex))
    };
}
