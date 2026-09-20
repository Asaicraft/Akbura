using AkburaTemplateNamespace.ViewModels;
using Avalonia.Controls;

namespace AkburaTemplateNamespace.Views;

internal static class MainViewHost
{
    public static Control Create() => new UserControl
    {
        Content = new MainView()
    };

    public static Control CreateWithDataContext(MainViewModel viewModel)
    {
        var view = Create();
        view.DataContext = viewModel;
        return view;
    }
}
