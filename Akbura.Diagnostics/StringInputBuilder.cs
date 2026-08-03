using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;
using Avalonia.Media;

namespace Akbura.Diagnostics;

/// <summary>
/// Edits string values with a text box.
/// </summary>
public sealed class StringInputBuilder : InputBuilder
{
    public override StreamGeometry Icon =>
        DiagnosticResources.StringInputBuilderIcon;

    public override Type OutputType => typeof(string);

    protected override Control BuildCore(InputRequest request)
    {
        var textBox = new TextBox();

        var binding = new CompiledBinding(InputValueBindingPath)
        {
            Source = textBox,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };

        textBox.Bind(TextBox.TextProperty, binding);

        return textBox;
    }
}
