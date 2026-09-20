using AkburaTemplateNamespace.ViewModels;
using AkburaTemplateNamespace.Views;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace AkburaTemplateNamespace;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? parameter) => parameter switch
    {
        MainViewModel viewModel => new MainView { DataContext = viewModel },
        _ => null,
    };

    public bool Match(object? data) => data is MainViewModel;
}
