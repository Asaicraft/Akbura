using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Akbura.Diagnostics;

internal sealed partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
