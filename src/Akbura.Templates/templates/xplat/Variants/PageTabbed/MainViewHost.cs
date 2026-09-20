using AkburaTemplateNamespace.ViewModels;
using Avalonia.Controls;

namespace AkburaTemplateNamespace.Views;

internal static class MainViewHost
{
    public static Control Create()
    {
        var tabs = new TabbedPage
        {
            Pages = new Page[]
            {
                new ContentPage { Header = "Home", Content = new MainView() },
                new ContentPage
                {
                    Header = "Settings",
                    Content = new TextBlock
                    {
                        Text = "Settings for your Akbura application",
                        Margin = new Avalonia.Thickness(24)
                    }
                }
            }
        };

        return new PageNavigationHost { Page = tabs };
    }

    public static Control CreateWithDataContext(MainViewModel viewModel)
    {
        var view = Create();
        view.DataContext = viewModel;
        return view;
    }
}
