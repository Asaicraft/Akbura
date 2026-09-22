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

    private static Control Create(MainViewModel? viewModel) => new PageNavigationHost
    {
        Page = new ContentPage
        {
            Header = "Home",
            Content = CreateMainView(viewModel)
        }
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
