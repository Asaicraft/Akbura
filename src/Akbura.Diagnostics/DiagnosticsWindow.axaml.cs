using Akbura.ComponentTree;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Akbura.Diagnostics;

internal sealed partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
        : this(
            new InputBuilderProvider(
                InputBuilderProvider.CreateDefaultBuilders()),
            services: null)
    {
    }

    internal DiagnosticsWindow(
        IInputBuilderProvider inputBuilders,
        IServiceProvider? services)
    {
        ArgumentNullException.ThrowIfNull(inputBuilders);

        AkburaComponentRegistry.ExcludeTopLevel(this);
        AvaloniaXamlLoader.Load(this);

        Content = new DiagnosticsRoot
        {
            InputBuilders = inputBuilders,
            InputServices = services,
        };
    }
}
