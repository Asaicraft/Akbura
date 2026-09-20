using Avalonia.Controls;
using Avalonia.Controls.Templates;
using AkburaTemplateNamespace.ViewModels;
using AkburaTemplateNamespace.Views;

namespace AkburaTemplateNamespace;

/// <summary>
/// Maps the demo view model to its generated Akbura view.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        return param is MainViewModel ? new MainView { DataContext = param } : null;
    }

    public bool Match(object? data) => data is MainViewModel;
}
