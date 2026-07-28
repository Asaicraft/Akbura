using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.ComponentModel;
using System.Globalization;

namespace Akbura.Markup;

[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public static class AkburaCompiledBinding
{
    public static CompiledBinding CreateField<TSource, TValue>(
        string name,
        Func<TSource, TValue> getter,
        object? source = null,
        IValueConverter? converter = null,
        BindingMode mode = BindingMode.Default,
        BindingPriority priority = BindingPriority.LocalValue,
        CultureInfo? converterCulture = null,
        object? converterParameter = null,
        object? fallbackValue = null,
        string? stringFormat = null,
        object? targetNullValue = null,
        UpdateSourceTrigger updateSourceTrigger =
            UpdateSourceTrigger.Default,
        int delay = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(getter);

        var property = new ClrPropertyInfo(
            name,
            target => getter((TSource)target),
            setter: null,
            typeof(TValue));
        var path = new CompiledBindingPathBuilder()
            .Property(
                property,
                static (reference, info) =>
                    new DelegatePropertyAccessor(
                        reference,
                        info))
            .Build();

        return new CompiledBinding(path)
        {
            Source = source ?? AvaloniaProperty.UnsetValue,
            Converter = converter,
            ConverterCulture = converterCulture,
            ConverterParameter = converterParameter,
            FallbackValue =
                fallbackValue ?? AvaloniaProperty.UnsetValue,
            Mode = mode,
            Priority = priority,
            StringFormat = stringFormat,
            TargetNullValue =
                targetNullValue ?? AvaloniaProperty.UnsetValue,
            UpdateSourceTrigger = updateSourceTrigger,
            Delay = delay,
        };
    }

    private sealed class DelegatePropertyAccessor :
        IPropertyAccessor
    {
        private readonly WeakReference<object?> _target;
        private readonly IPropertyInfo _property;
        private Action<object?>? _listener;
        private INotifyPropertyChanged? _notifyingTarget;

        public DelegatePropertyAccessor(
            WeakReference<object?> target,
            IPropertyInfo property)
        {
            _target = target;
            _property = property;
        }

        public Type? PropertyType => _property.PropertyType;

        public object? Value
        {
            get
            {
                return _target.TryGetTarget(out var target) &&
                    target != null
                        ? _property.Get(target)
                        : AvaloniaProperty.UnsetValue;
            }
        }

        public bool SetValue(
            object? value,
            BindingPriority priority)
        {
            if (!_property.CanSet ||
                !_target.TryGetTarget(out var target) ||
                target == null)
            {
                return false;
            }

            _property.Set(target, value);
            return true;
        }

        public void Subscribe(Action<object?> listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            Unsubscribe();

            _listener = listener;
            if (_target.TryGetTarget(out var target) &&
                target is INotifyPropertyChanged notifyingTarget)
            {
                _notifyingTarget = notifyingTarget;
                _notifyingTarget.PropertyChanged +=
                    OnPropertyChanged;
            }

            listener(Value);
        }

        public void Unsubscribe()
        {
            if (_notifyingTarget != null)
            {
                _notifyingTarget.PropertyChanged -=
                    OnPropertyChanged;
                _notifyingTarget = null;
            }

            _listener = null;
        }

        public void Dispose()
        {
            Unsubscribe();
        }

        private void OnPropertyChanged(
            object? sender,
            PropertyChangedEventArgs eventArgs)
        {
            if (string.IsNullOrEmpty(eventArgs.PropertyName) ||
                string.Equals(
                    eventArgs.PropertyName,
                    _property.Name,
                    StringComparison.Ordinal))
            {
                _listener?.Invoke(Value);
            }
        }
    }
}
