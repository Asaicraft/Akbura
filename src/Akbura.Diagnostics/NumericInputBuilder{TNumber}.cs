using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;
using System.Globalization;
using System.Numerics;

namespace Akbura.Diagnostics;

/// <summary>
/// Edits numeric values with an Avalonia numeric input.
/// </summary>
public sealed class NumericInputBuilder<TNumber> : InputBuilder
    where TNumber : struct, INumber<TNumber>, IMinMaxValue<TNumber>
{
    private static readonly IValueConverter NullableConverter =
        new NumericValueConverter(true);

    private static readonly IValueConverter NonNullableConverter =
        new NumericValueConverter(false);

    private readonly bool _isNullable;
    private readonly decimal _minimum;
    private readonly decimal _maximum;
    private readonly decimal _increment;
    private readonly IValueConverter _converter;

    public NumericInputBuilder(
        bool isNullable = false,
        decimal? minimum = null,
        decimal? maximum = null,
        decimal? increment = null)
    {
        _isNullable = isNullable;

        _minimum = minimum
            ?? decimal.CreateSaturating(TNumber.MinValue);

        _maximum = maximum
            ?? decimal.CreateSaturating(TNumber.MaxValue);

        _increment = increment
            ?? GetDefaultIncrement();

        if (_minimum > _maximum)
        {
            throw new ArgumentException(
                "Minimum cannot be greater than maximum.");
        }

        if (_increment <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(increment),
                "Increment must be greater than zero.");
        }

        _converter = isNullable
            ? NullableConverter
            : NonNullableConverter;
    }

    public override Type OutputType =>
        _isNullable
            ? typeof(TNumber?)
            : typeof(TNumber);

    protected override Control BuildCore(InputRequest request)
    {
        var input = new NumericUpDown
        {
            Minimum = _minimum,
            Maximum = _maximum,
            Increment = _increment,
            ClipValueToMinMax = true,
            FormatString = GetFormatString()
        };

        var binding = new CompiledBinding(InputValueBindingPath)
        {
            Source = input,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            Converter = _converter
        };

        input.Bind(NumericUpDown.ValueProperty, binding);

        return input;
    }

    private static decimal GetDefaultIncrement()
    {
        return IsIntegerType()
            ? 1m
            : 0.1m;
    }

    private static string GetFormatString()
    {
        return IsIntegerType()
            ? "0"
            : "0.############################";
    }

    private static bool IsIntegerType()
    {
        var type = typeof(TNumber);

        return type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(nint)
            || type == typeof(nuint);
    }

    private sealed class NumericValueConverter : IValueConverter
    {
        private readonly bool _isNullable;

        public NumericValueConverter(bool isNullable)
        {
            _isNullable = isNullable;
        }

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            if (value is null)
            {
                return null;
            }

            try
            {
                if (value is TNumber number)
                {
                    return decimal.CreateChecked(number);
                }

                return System.Convert.ToDecimal(value, culture);
            }
            catch (Exception exception) when (
                exception is InvalidCastException
                or FormatException
                or OverflowException
                or NotSupportedException)
            {
                return CreateValidationError(exception);
            }
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            if (value is null)
            {
                return _isNullable
                    ? null
                    : BindingOperations.DoNothing;
            }

            try
            {
                var decimalValue = value is decimal number
                    ? number
                    : System.Convert.ToDecimal(value, culture);

                return TNumber.CreateChecked(decimalValue);
            }
            catch (Exception exception) when (
                exception is InvalidCastException
                or FormatException
                or OverflowException
                or NotSupportedException)
            {
                return CreateValidationError(exception);
            }
        }

        private static BindingNotification CreateValidationError(
            Exception exception)
        {
            return new BindingNotification(
                exception,
                BindingErrorType.DataValidationError);
        }
    }
}
