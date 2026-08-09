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

    private protected AkcssUtilityValueSource(
        bool recreateOnRefresh,
        bool hasBindingPriorityMetadata = false)
    {
        RecreateOnRefresh = recreateOnRefresh;
        HasBindingPriorityMetadata = hasBindingPriorityMetadata;
    }

    internal bool HasValue { get; private protected set; }

    internal object? Value { get; private protected set; }

    internal bool RecreateOnRefresh { get; }

    internal bool HasBindingPriorityMetadata { get; }

    internal BindingPriority BindingPriority { get; private protected set; }

    internal void Attach(object target, Action changed)
    {
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        AttachCore(target);
    }

    internal void Refresh(object target)
    {
        if (RecreateOnRefresh)
        {
            DetachCore(target);
            HasValue = false;
            Value = null;
            AttachCore(target);
        }
    }

    internal void Detach(object target)
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

    private protected void SetBindingPriority(BindingPriority priority)
    {
        BindingPriority = AkcssBindingPriority.Validate(priority);
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

    private protected abstract void AttachCore(object target);

    private protected abstract void DetachCore(object target);

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
    /// Creates a direct source that reads its value and priority from one
    /// markup extension invocation.
    /// </summary>
    public static AkcssUtilityValueSource CreateWithPriority<TValue>(
        Func<Control, AkcssUtilityPrefixInvocation<TValue>> factory,
        bool recreateOnRefresh = false)
    {
        return new DirectValueSource<TValue>(
            factory,
            recreateOnRefresh);
    }

    /// <summary>
    /// Creates a direct value source for an AKCSS target that is not required
    /// to derive from <see cref="Control"/>.
    /// </summary>
    public static AkcssUtilityValueSource CreateForObject<TValue>(
        Func<object, TValue> factory,
        bool recreateOnRefresh = false)
    {
        return new ObjectTargetDirectValueSource<TValue>(
            factory,
            recreateOnRefresh);
    }

    /// <summary>
    /// Creates a priority-aware direct source for a non-control AKCSS target.
    /// </summary>
    public static AkcssUtilityValueSource CreateForObjectWithPriority<TValue>(
        Func<object, AkcssUtilityPrefixInvocation<TValue>> factory,
        bool recreateOnRefresh = false)
    {
        return new ObjectTargetDirectValueSource<TValue>(
            factory,
            recreateOnRefresh);
    }

    /// <summary>
    /// Creates a source whose late-bound markup extension result is converted
    /// to the utility value type.
    /// </summary>
    public static AkcssUtilityValueSource CreateObject<TValue>(
        Func<Control, object?> factory,
        Func<object?, TValue> converter,
        bool recreateOnRefresh = false)
    {
        return new ObjectValueSource<TValue>(
            factory,
            converter,
            recreateOnRefresh);
    }

    /// <summary>
    /// Creates a late-bound source whose value and priority come from one
    /// markup extension invocation.
    /// </summary>
    public static AkcssUtilityValueSource CreateObjectWithPriority<TValue>(
        Func<Control, AkcssUtilityPrefixInvocation<object?>> factory,
        Func<object?, TValue> converter,
        bool recreateOnRefresh = false)
    {
        return new ObjectValueSource<TValue>(
            factory,
            converter,
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
    /// Creates an observable source whose observable and priority come from
    /// one markup extension invocation.
    /// </summary>
    public static AkcssUtilityValueSource CreateObservableWithPriority<TSource, TValue>(
        Func<Control, AkcssUtilityPrefixInvocation<IObservable<TSource>?>> factory,
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
    /// Creates an object-observable source whose observable and priority come
    /// from one markup extension invocation.
    /// </summary>
    public static AkcssUtilityValueSource CreateObservableObjectWithPriority<TValue>(
        Func<Control, AkcssUtilityPrefixInvocation<IObservable<object?>?>> factory,
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

    /// <summary>
    /// Creates a binding source whose binding and priority come from one
    /// markup extension invocation.
    /// </summary>
    public static AkcssUtilityValueSource CreateBindingWithPriority<TValue>(
        Func<Control, AkcssUtilityPrefixInvocation<BindingBase>> factory,
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
        private readonly Func<Control, AkcssUtilityPrefixInvocation<TValue>>? _priorityFactory;

        public DirectValueSource(
            Func<Control, TValue> factory,
            bool recreateOnRefresh)
            : base(recreateOnRefresh)
        {
            _factory = factory ??
                throw new ArgumentNullException(nameof(factory));
        }

        public DirectValueSource(
            Func<Control, AkcssUtilityPrefixInvocation<TValue>> factory,
            bool recreateOnRefresh)
            : base(recreateOnRefresh, hasBindingPriorityMetadata: true)
        {
            _factory = null!;
            _priorityFactory = factory ??
                throw new ArgumentNullException(nameof(factory));
        }

        private protected override void AttachCore(object target)
        {
            var control = GetControl(target);
            if (_priorityFactory == null)
            {
                SetValue(_factory(control));
                return;
            }

            var invocation = _priorityFactory(control);
            SetBindingPriority(invocation.Priority);
            SetValue(invocation.Value);
        }

        private protected override void DetachCore(object target)
        {
        }
    }

    private sealed class ObjectTargetDirectValueSource<TValue>
        : AkcssUtilityValueSource
    {
        private readonly Func<object, TValue> _factory;
        private readonly Func<object, AkcssUtilityPrefixInvocation<TValue>>? _priorityFactory;

        public ObjectTargetDirectValueSource(
            Func<object, TValue> factory,
            bool recreateOnRefresh)
            : base(recreateOnRefresh)
        {
            _factory = factory ??
                throw new ArgumentNullException(nameof(factory));
        }

        public ObjectTargetDirectValueSource(
            Func<object, AkcssUtilityPrefixInvocation<TValue>> factory,
            bool recreateOnRefresh)
            : base(recreateOnRefresh, hasBindingPriorityMetadata: true)
        {
            _factory = null!;
            _priorityFactory = factory ??
                throw new ArgumentNullException(nameof(factory));
        }

        private protected override void AttachCore(object target)
        {
            if (_priorityFactory == null)
            {
                SetValue(_factory(target));
                return;
            }

            var invocation = _priorityFactory(target);
            SetBindingPriority(invocation.Priority);
            SetValue(invocation.Value);
        }

        private protected override void DetachCore(object target)
        {
        }
    }

    private sealed class ObservableValueSource<TSource, TValue>
        : AkcssUtilityValueSource,
          IObserver<TSource>
    {
        private readonly Func<Control, IObservable<TSource>?> _factory;
        private readonly Func<Control, AkcssUtilityPrefixInvocation<IObservable<TSource>?>>? _priorityFactory;
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

        public ObservableValueSource(
            Func<Control, AkcssUtilityPrefixInvocation<IObservable<TSource>?>> factory,
            Func<TSource, TValue> converter,
            bool recreateOnRefresh)
            : base(recreateOnRefresh, hasBindingPriorityMetadata: true)
        {
            _factory = null!;
            _priorityFactory = factory ??
                throw new ArgumentNullException(nameof(factory));
            _converter = converter ??
                throw new ArgumentNullException(nameof(converter));
        }

        private protected override void AttachCore(object target)
        {
            var control = GetControl(target);
            IObservable<TSource>? observable;
            if (_priorityFactory == null)
            {
                observable = _factory(control);
            }
            else
            {
                var invocation = _priorityFactory(control);
                SetBindingPriority(invocation.Priority);
                observable = invocation.Value;
            }

            _subscription = observable?.Subscribe(this);
        }

        private protected override void DetachCore(object target)
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

    private sealed class ObjectValueSource<TValue>
        : AkcssUtilityValueSource
    {
        private readonly Func<Control, object?> _factory;
        private readonly Func<Control, AkcssUtilityPrefixInvocation<object?>>? _priorityFactory;
        private readonly Func<object?, TValue> _converter;

        public ObjectValueSource(
            Func<Control, object?> factory,
            Func<object?, TValue> converter,
            bool recreateOnRefresh)
            : base(recreateOnRefresh)
        {
            _factory = factory ??
                throw new ArgumentNullException(nameof(factory));
            _converter = converter ??
                throw new ArgumentNullException(nameof(converter));
        }

        public ObjectValueSource(
            Func<Control, AkcssUtilityPrefixInvocation<object?>> factory,
            Func<object?, TValue> converter,
            bool recreateOnRefresh)
            : base(recreateOnRefresh, hasBindingPriorityMetadata: true)
        {
            _factory = null!;
            _priorityFactory = factory ??
                throw new ArgumentNullException(nameof(factory));
            _converter = converter ??
                throw new ArgumentNullException(nameof(converter));
        }

        private protected override void AttachCore(object target)
        {
            var control = GetControl(target);
            if (_priorityFactory == null)
            {
                SetValue(_converter(_factory(control)));
                return;
            }

            var invocation = _priorityFactory(control);
            SetBindingPriority(invocation.Priority);
            SetValue(_converter(invocation.Value));
        }

        private protected override void DetachCore(object target)
        {
        }
    }

    private sealed class BindingValueSource<TValue>
        : AkcssUtilityValueSource,
          IObserver<object?>
    {
        private readonly Func<Control, BindingBase> _factory;
        private readonly Func<Control, AkcssUtilityPrefixInvocation<BindingBase>>? _priorityFactory;
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

        public BindingValueSource(
            Func<Control, AkcssUtilityPrefixInvocation<BindingBase>> factory,
            AttachedProperty<object?> property,
            Func<object?, TValue> converter,
            bool recreateOnRefresh)
            : base(recreateOnRefresh, hasBindingPriorityMetadata: true)
        {
            _factory = null!;
            _priorityFactory = factory ??
                throw new ArgumentNullException(nameof(factory));
            _property = property ??
                throw new ArgumentNullException(nameof(property));
            _converter = converter ??
                throw new ArgumentNullException(nameof(converter));
        }

        private protected override void AttachCore(object target)
        {
            var control = GetControl(target);
            BindingBase binding;
            if (_priorityFactory == null)
            {
                binding = _factory(control);
            }
            else
            {
                var invocation = _priorityFactory(control);
                SetBindingPriority(invocation.Priority);
                binding = invocation.Value;
            }

            _binding = control.Bind(
                _property,
                binding);
            _subscription = control
                .GetObservable(_property)
                .Subscribe(this);
        }

        private protected override void DetachCore(object target)
        {
            var control = GetControl(target);
            _subscription?.Dispose();
            _subscription = null;
            _binding?.Dispose();
            _binding = null;
            control.ClearValue(_property);
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

    private static Control GetControl(object target)
    {
        return target as Control ??
            throw new ArgumentException(
                $"This AKCSS value source requires a '{typeof(Control)}' target.",
                nameof(target));
    }
}
