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
        var tabs = new TabbedPage
        {
            Pages = new Page[]
            {
                new ContentPage
                {
                    Header = "Home",
                    Content = CreateMainView(viewModel)
                },
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
