using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;
using Avalonia.Media;
using System.Globalization;

namespace Akbura.Diagnostics;

/// <summary>
/// Provides a text and JSON based editor for values that do not have a more
/// specific input builder.
/// </summary>
public sealed class UniversalInputBuilder : InputBuilder
{
    public override Type OutputType => typeof(object);

    public override bool CanProvide(InputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StateValueConverter.CanEdit(request.RequestedType);
    }

    protected override Control BuildCore(InputRequest request)
    {
        return BuildCore(request, existingValue: null);
    }

    protected override Control BuildCore(
        InputRequest request,
        object? existingValue)
    {
        var conversionType = GetConversionType(
            request.EditorType,
            existingValue);
        var converter = new TextValueConverter(conversionType);
        var multiline = StateValueConverter.ShouldUseJson(conversionType);
        var textBox = new TextBox
        {
            AcceptsReturn = multiline,
            TextWrapping = multiline
                ? TextWrapping.Wrap
                : TextWrapping.NoWrap,
            MinHeight = multiline ? 96d : 32d,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontFamily = multiline
                ? new FontFamily("Cascadia Mono, Consolas")
                : FontFamily.Default,
        };

        var binding = new CompiledBinding(InputValueBindingPath)
        {
            Source = textBox,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            Converter = converter,
        };

        textBox.Bind(TextBox.TextProperty, binding);
        return textBox;
    }

    private static Type GetConversionType(
        Type requestedType,
        object? existingValue)
    {
        if (existingValue is not null &&
            (requestedType == typeof(object) ||
             requestedType.IsAbstract ||
             requestedType.IsInterface))
        {
            return existingValue.GetType();
        }

        return requestedType;
    }

    private sealed class TextValueConverter : IValueConverter
    {
        private readonly Type _valueType;

        public TextValueConverter(Type valueType)
        {
            _valueType = valueType;
        }

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            return StateValueConverter.FormatForEditor(value, _valueType);
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            if (StateValueConverter.TryParse(
                    value as string,
                    _valueType,
                    out var result,
                    out var error))
            {
                return result;
            }

            return new BindingNotification(
                new FormatException(error),
                BindingErrorType.DataValidationError);
        }
    }
}
