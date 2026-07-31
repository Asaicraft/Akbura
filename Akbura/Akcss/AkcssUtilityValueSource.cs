using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using System.ComponentModel;
using System.Runtime.ExceptionServices;

namespace Akbura.Akcss;

/// <summary>
/// Supplies one generated AKCSS utility argument or variant value.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public abstract class AkcssUtilityValueSource
{
    private Action? _changed;

    private protected AkcssUtilityValueSource(bool recreateOnRefresh)
    {
        RecreateOnRefresh = recreateOnRefresh;
    }

    internal bool HasValue { get; private protected set; }

    internal object? Value { get; private protected set; }

    internal bool RecreateOnRefresh { get; }

    internal void Attach(Control target, Action changed)
    {
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        AttachCore(target);
    }

    internal void Refresh(Control target)
    {
        if (RecreateOnRefresh)
        {
            DetachCore(target);
            HasValue = false;
            Value = null;
            AttachCore(target);
        }
    }

    internal void Detach(Control target)
    {
        DetachCore(target);
        HasValue = false;
        Value = null;
        _changed = null;
    }

    private protected void SetValue(object? value)
    {
        HasValue = true;
        Value = value;
        _changed?.Invoke();
    }

    private protected void ClearValue()
    {
        var changed = HasValue;
        HasValue = false;
        Value = null;
        if (changed)
        {
            _changed?.Invoke();
        }
    }

    private protected void Fail(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ExceptionDispatchInfo.Capture(error).Throw();
    }

    private protected abstract void AttachCore(Control target);

    private protected abstract void DetachCore(Control target);

    /// <summary>
    /// Creates a source whose markup extension returns the utility value directly.
    /// </summary>
    public static AkcssUtilityValueSource Create<TValue>(
        Func<Control, TValue> factory,
        bool recreateOnRefresh = false)
    {
        return new DirectValueSource<TValue>(
            factory,
            recreateOnRefresh);
    }

    /// <summary>
    /// Creates a source whose markup extension returns an observable value.
    /// </summary>
    public static AkcssUtilityValueSource CreateObservable<TSource, TValue>(
        Func<Control, IObservable<TSource>?> factory,
        Func<TSource, TValue> converter,
        bool recreateOnRefresh = false)
    {
        return new ObservableValueSource<TSource, TValue>(
            factory,
            converter,
            recreateOnRefresh);
    }

    /// <summary>
    /// Creates a source whose markup extension returns an object observable.
    /// </summary>
    public static AkcssUtilityValueSource CreateObservableObject<TValue>(
        Func<Control, IObservable<object?>?> factory,
        Func<object?, TValue> converter,
        bool recreateOnRefresh = false)
    {
        return new ObservableValueSource<object?, TValue>(
            factory,
            converter,
            recreateOnRefresh);
    }

    /// <summary>
    /// Creates a source whose markup extension returns an Avalonia binding.
    /// </summary>
    public static AkcssUtilityValueSource CreateBinding<TValue>(
        Func<Control, BindingBase> factory,
        AttachedProperty<object?> property,
        Func<object?, TValue> converter,
        bool recreateOnRefresh = false)
    {
        return new BindingValueSource<TValue>(
            factory,
            property,
            converter,
            recreateOnRefresh);
    }

    private sealed class DirectValueSource<TValue>
        : AkcssUtilityValueSource
    {
        private readonly Func<Control, TValue> _factory;

        public DirectValueSource(
            Func<Control, TValue> factory,
            bool recreateOnRefresh)
            : base(recreateOnRefresh)
        {
            _factory = factory ??
                throw new ArgumentNullException(nameof(factory));
        }

        private protected override void AttachCore(Control target)
        {
            SetValue(_factory(target));
        }

        private protected override void DetachCore(Control target)
        {
        }
    }

    private sealed class ObservableValueSource<TSource, TValue>
        : AkcssUtilityValueSource,
          IObserver<TSource>
    {
        private readonly Func<Control, IObservable<TSource>?> _factory;
        private readonly Func<TSource, TValue> _converter;
        private IDisposable? _subscription;

        public ObservableValueSource(
            Func<Control, IObservable<TSource>?> factory,
            Func<TSource, TValue> converter,
            bool recreateOnRefresh)
            : base(recreateOnRefresh)
        {
            _factory = factory ??
                throw new ArgumentNullException(nameof(factory));
            _converter = converter ??
                throw new ArgumentNullException(nameof(converter));
        }

        private protected override void AttachCore(Control target)
        {
            _subscription = _factory(target)?.Subscribe(this);
        }

        private protected override void DetachCore(Control target)
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        void IObserver<TSource>.OnNext(TSource value)
        {
            SetValue(_converter(value));
        }

        void IObserver<TSource>.OnError(Exception error)
        {
            Fail(error);
        }

        void IObserver<TSource>.OnCompleted()
        {
        }
    }

    private sealed class BindingValueSource<TValue>
        : AkcssUtilityValueSource,
          IObserver<object?>
    {
        private readonly Func<Control, BindingBase> _factory;
        private readonly AttachedProperty<object?> _property;
        private readonly Func<object?, TValue> _converter;
        private IDisposable? _binding;
        private IDisposable? _subscription;

        public BindingValueSource(
            Func<Control, BindingBase> factory,
            AttachedProperty<object?> property,
            Func<object?, TValue> converter,
            bool recreateOnRefresh)
            : base(recreateOnRefresh)
        {
            _factory = factory ??
                throw new ArgumentNullException(nameof(factory));
            _property = property ??
                throw new ArgumentNullException(nameof(property));
            _converter = converter ??
                throw new ArgumentNullException(nameof(converter));
        }

        private protected override void AttachCore(Control target)
        {
            _binding = target.Bind(
                _property,
                _factory(target));
            _subscription = target
                .GetObservable(_property)
                .Subscribe(this);
        }

        private protected override void DetachCore(Control target)
        {
            _subscription?.Dispose();
            _subscription = null;
            _binding?.Dispose();
            _binding = null;
            target.ClearValue(_property);
        }

        void IObserver<object?>.OnNext(object? value)
        {
            if (ReferenceEquals(
                value,
                AvaloniaProperty.UnsetValue))
            {
                ClearValue();
                return;
            }

            if (value == null && default(TValue) is not null)
            {
                ClearValue();
                return;
            }

            SetValue(_converter(value));
        }

        void IObserver<object?>.OnError(Exception error)
        {
            Fail(error);
        }

        void IObserver<object?>.OnCompleted()
        {
        }
    }
}
