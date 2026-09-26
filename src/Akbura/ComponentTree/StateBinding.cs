using Avalonia;
using Avalonia.Data;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Akbura.ComponentTree;

[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public static class StateBindings
{
    public static IDisposable ObserveAvaloniaProperty(
        AvaloniaObject source,
        AvaloniaProperty property,
        Action changed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(changed);
        return new AvaloniaPropertySubscription(source, property, changed);
    }

    public static IDisposable CombineSubscriptions(IDisposable[] subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        return new CombinedSubscription(subscriptions);
    }

    public static State<T> CreateBind<T>(
        Func<object?, T> convert,
        Action<T> write,
        Func<object?> root,
        Func<CompiledBindingPath> path,
        int pathLength,
        bool requiresNonNullSource,
        Func<Action, IDisposable>? observeRoot = null)
    {
        ArgumentNullException.ThrowIfNull(convert);
        ArgumentNullException.ThrowIfNull(write);
        return Create(
            read: null,
            convert,
            write,
            root,
            path,
            pathLength,
            requiresNonNullSource,
            observeRoot,
            StateBindingDirection.Bind);
    }

    public static State<T> CreateOut<T>(
        Func<object?, T> convert,
        Func<object?> root,
        Func<CompiledBindingPath> path,
        int pathLength,
        bool requiresNonNullSource,
        Func<Action, IDisposable>? observeRoot = null)
    {
        ArgumentNullException.ThrowIfNull(convert);
        return Create(
            read: null,
            convert,
            write: null,
            root,
            path,
            pathLength,
            requiresNonNullSource,
            observeRoot,
            StateBindingDirection.Out);
    }

    public static State<T> CreateIn<T>(
        Func<T>? read,
        Action<T> write,
        Func<object?> root,
        Func<CompiledBindingPath> path,
        int pathLength,
        bool requiresNonNullSource,
        Func<Action, IDisposable>? observeRoot = null)
    {
        ArgumentNullException.ThrowIfNull(write);
        return Create(
            read,
            convert: null,
            write,
            root,
            path,
            pathLength,
            requiresNonNullSource,
            observeRoot,
            StateBindingDirection.In);
    }

    public static State<T> CreateOutObservable<T>(
        Func<IObservable<T>?> source,
        Func<object?> root,
        Func<CompiledBindingPath> path,
        int pathLength,
        bool requiresNonNullSource,
        Func<Action, IDisposable>? observeRoot = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(pathLength);

        var state = new State<T>(default!);
        var binding = new ObservableStateBinding<T>(
            state,
            source,
            root,
            path,
            pathLength,
            requiresNonNullSource,
            observeRoot);
        state.RetainResource(binding);
        return state;
    }

    private static State<T> Create<T>(
        Func<T>? read,
        Func<object?, T>? convert,
        Action<T>? write,
        Func<object?> root,
        Func<CompiledBindingPath> path,
        int pathLength,
        bool requiresNonNullSource,
        Func<Action, IDisposable>? observeRoot,
        StateBindingDirection direction)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(pathLength);

        var state = new State<T>(default!);
        var binding = new DirectionalStateBinding<T>(
            state,
            read,
            convert,
            write,
            root,
            path,
            pathLength,
            requiresNonNullSource,
            observeRoot,
            direction);
        state.RetainResource(binding);
        return state;
    }

    private sealed class CombinedSubscription : IDisposable
    {
        private IDisposable[]? _subscriptions;

        public CombinedSubscription(IDisposable[] subscriptions)
        {
            _subscriptions = subscriptions;
        }

        public void Dispose()
        {
            var subscriptions = Interlocked.Exchange(ref _subscriptions, null);
            if (subscriptions == null)
            {
                return;
            }

            List<Exception>? failures = null;
            for (var index = subscriptions.Length - 1; index >= 0; index--)
            {
                try
                {
                    subscriptions[index].Dispose();
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            if (failures != null)
            {
                throw new AggregateException(failures);
            }
        }
    }

    private sealed class AvaloniaPropertySubscription : IDisposable
    {
        private AvaloniaObject? _source;
        private readonly AvaloniaProperty _property;
        private readonly Action _changed;

        public AvaloniaPropertySubscription(
            AvaloniaObject source,
            AvaloniaProperty property,
            Action changed)
        {
            _source = source;
            _property = property;
            _changed = changed;
            source.PropertyChanged += OnPropertyChanged;
        }

        public void Dispose()
        {
            var source = Interlocked.Exchange(ref _source, null);
            if (source != null)
            {
                source.PropertyChanged -= OnPropertyChanged;
            }
        }

        private void OnPropertyChanged(
            object? sender,
            AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property == _property)
            {
                _changed();
            }
        }
    }
}

internal enum StateBindingDirection : byte
{
    Bind,
    In,
    Out,
}

internal interface IStateResource
{
    void Suspend();

    void Resume();
}

internal sealed class DirectionalStateBinding<T> : IStateResource
{
    private readonly State<T> _state;
    private readonly Func<T>? _read;
    private readonly Func<object?, T>? _convert;
    private readonly Action<T>? _write;
    private readonly CompiledStatePathObserver _path;
    private readonly StateBindingDirection _direction;
    private IDisposable? _stateSubscription;
    private bool _updatingFromSource;
    private bool _refreshingTarget;
    private bool _initializing;
    private bool _isActive;

    public DirectionalStateBinding(
        State<T> state,
        Func<T>? read,
        Func<object?, T>? convert,
        Action<T>? write,
        Func<object?> root,
        Func<CompiledBindingPath> path,
        int pathLength,
        bool requiresNonNullSource,
        Func<Action, IDisposable>? observeRoot,
        StateBindingDirection direction)
    {
        _state = state;
        _read = read;
        _convert = convert;
        _write = write;
        _direction = direction;
        _path = new CompiledStatePathObserver(
            root,
            path,
            pathLength,
            requiresNonNullSource,
            nullIsDisconnected: direction == StateBindingDirection.In,
            observeRoot,
            OnPathChanged);
        Initialize();
    }

    public void Suspend()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        _stateSubscription?.Dispose();
        _stateSubscription = null;
        _path.Suspend();
    }

    public void Resume()
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _path.Resume();
        if (_direction is StateBindingDirection.Bind or StateBindingDirection.Out)
        {
            PullSourceValue();
        }

        SubscribeToState();
    }

    private void Initialize()
    {
        _initializing = true;
        _isActive = true;
        try
        {
            _path.Resume();
            if (TryReadSourceValue(out var value))
            {
                _state.InitializeValue(value);
            }
        }
        finally
        {
            _initializing = false;
        }

        SubscribeToState();
    }

    private void SubscribeToState()
    {
        if (_direction is StateBindingDirection.Bind or StateBindingDirection.In)
        {
            _stateSubscription = _state.Subscribe(PushStateValue);
        }
    }

    private void OnPathChanged()
    {
        if (_initializing || _refreshingTarget)
        {
            return;
        }

        if (_direction is StateBindingDirection.Bind or StateBindingDirection.Out)
        {
            PullSourceValue();
        }
    }

    private void PullSourceValue()
    {
        if (!TryReadSourceValue(out var value))
        {
            return;
        }

        _updatingFromSource = true;
        try
        {
            _state.Value = value;
        }
        finally
        {
            _updatingFromSource = false;
        }
    }

    private bool TryReadSourceValue(out T value)
    {
        if (!_path.IsConnected)
        {
            value = default!;
            return false;
        }

        if (_convert != null)
        {
            value = _convert(_path.CurrentValue);
            return true;
        }

        if (_read != null)
        {
            value = _read();
            return true;
        }

        value = default!;
        return false;
    }

    private void PushStateValue(T value)
    {
        if (_updatingFromSource || _write == null || !_path.IsConnected)
        {
            return;
        }

        if (_direction != StateBindingDirection.Bind)
        {
            _write(value);
            return;
        }

        _refreshingTarget = true;
        try
        {
            _write(value);
            _path.UpdateTarget();
        }
        finally
        {
            _refreshingTarget = false;
        }

        PullSourceValue();
    }
}

internal sealed class ObservableStateBinding<T> : IStateResource
{
    private readonly State<T> _state;
    private readonly Func<IObservable<T>?> _source;
    private readonly CompiledStatePathObserver _path;
    private IDisposable? _sourceSubscription;
    private int _subscriptionGeneration;
    private bool _isActive;

    public ObservableStateBinding(
        State<T> state,
        Func<IObservable<T>?> source,
        Func<object?> root,
        Func<CompiledBindingPath> path,
        int pathLength,
        bool requiresNonNullSource,
        Func<Action, IDisposable>? observeRoot)
    {
        _state = state;
        _source = source;
        _path = new CompiledStatePathObserver(
            root,
            path,
            pathLength,
            requiresNonNullSource,
            nullIsDisconnected: false,
            observeRoot,
            RebindSource);
        Resume();
    }

    public void Suspend()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        DisposeSourceSubscription();
        _path.Suspend();
    }

    public void Resume()
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _path.Resume();
        RebindSource();
    }

    private void RebindSource()
    {
        DisposeSourceSubscription();
        if (!_isActive || !_path.IsConnected || GetCurrentSource() is not { } source)
        {
            return;
        }

        var generation = _subscriptionGeneration;
        var subscription = source.Subscribe(new SourceObserver(this, generation));
        if (!_isActive || generation != _subscriptionGeneration)
        {
            subscription.Dispose();
            return;
        }

        _sourceSubscription = subscription;
    }

    private IObservable<T>? GetCurrentSource()
    {
        return _path.HasCompiledValue
            ? _path.CurrentValue as IObservable<T>
            : _source();
    }

    private void DisposeSourceSubscription()
    {
        _subscriptionGeneration++;
        _sourceSubscription?.Dispose();
        _sourceSubscription = null;
    }

    private void OnNext(int generation, T value)
    {
        if (_isActive && generation == _subscriptionGeneration)
        {
            _state.Value = value;
        }
    }

    private void OnError(int generation, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!_isActive || generation != _subscriptionGeneration)
        {
            return;
        }

        DisposeSourceSubscription();
        ExceptionDispatchInfo.Capture(error).Throw();
    }

    private void OnCompleted(int generation)
    {
        if (_isActive && generation == _subscriptionGeneration)
        {
            DisposeSourceSubscription();
        }
    }

    private sealed class SourceObserver : IObserver<T>
    {
        private readonly ObservableStateBinding<T> _owner;
        private readonly int _generation;

        public SourceObserver(ObservableStateBinding<T> owner, int generation)
        {
            _owner = owner;
            _generation = generation;
        }

        public void OnNext(T value) => _owner.OnNext(_generation, value);

        public void OnError(Exception error) => _owner.OnError(_generation, error);

        public void OnCompleted() => _owner.OnCompleted(_generation);
    }
}

internal sealed class CompiledStatePathObserver
{
    private readonly Func<object?> _root;
    private readonly Func<CompiledBindingPath> _path;
    private readonly int _pathLength;
    private readonly bool _requiresNonNullSource;
    private readonly bool _nullIsDisconnected;
    private readonly Func<Action, IDisposable>? _observeRoot;
    private readonly Action _changed;
    private readonly StateBindingTarget _target;
    private BindingExpressionBase? _expression;
    private IDisposable? _rootSubscription;
    private object? _rootValue = StateBindingTarget.DisconnectedValue;
    private bool _rebinding;
    private bool _isActive;

    public CompiledStatePathObserver(
        Func<object?> root,
        Func<CompiledBindingPath> path,
        int pathLength,
        bool requiresNonNullSource,
        bool nullIsDisconnected,
        Func<Action, IDisposable>? observeRoot,
        Action changed)
    {
        _root = root;
        _path = path;
        _pathLength = pathLength;
        _requiresNonNullSource = requiresNonNullSource;
        _nullIsDisconnected = nullIsDisconnected;
        _observeRoot = observeRoot;
        _changed = changed;
        _target = new StateBindingTarget(OnTargetChanged);
    }

    public bool IsConnected { get; private set; }

    public bool HasCompiledValue => _pathLength > 0;

    public object? CurrentValue => _pathLength == 0
        ? _rootValue
        : _target.Value;

    public void Suspend()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        _rootSubscription?.Dispose();
        _rootSubscription = null;
        ClearExpression();
        IsConnected = false;
    }

    public void Resume()
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        if (_observeRoot != null)
        {
            _rootSubscription = _observeRoot(OnDependencyChanged);
        }

        Rebind();
    }

    public void UpdateTarget()
    {
        if (_pathLength == 0)
        {
            Rebind();
            return;
        }

        _expression?.UpdateTarget();
    }

    private void OnDependencyChanged()
    {
        if (!_isActive)
        {
            return;
        }

        Rebind();
        _changed();
    }

    private void OnTargetChanged()
    {
        if (!_isActive || _rebinding)
        {
            return;
        }

        UpdateConnection();
        _changed();
    }

    private void Rebind()
    {
        _rebinding = true;
        try
        {
            ClearExpression();
            var source = _root();
            _rootValue = source;
            if (_pathLength == 0)
            {
                IsConnected = !_requiresNonNullSource || source != null;
                return;
            }

            if (source == null)
            {
                IsConnected = false;
                return;
            }

            var binding = new CompiledBinding(_path())
            {
                Source = source,
                Mode = BindingMode.OneWay,
                FallbackValue = StateBindingTarget.DisconnectedValue,
            };
            _expression = _target.Bind(
                StateBindingTarget.ValueProperty,
                binding);
            UpdateConnection();
        }
        finally
        {
            _rebinding = false;
        }
    }

    private void UpdateConnection()
    {
        var value = _target.Value;
        IsConnected = !ReferenceEquals(
                value,
                StateBindingTarget.DisconnectedValue) &&
            (!_nullIsDisconnected || value != null);
    }

    private void ClearExpression()
    {
        _expression?.Dispose();
        _expression = null;
        _rootValue = StateBindingTarget.DisconnectedValue;
        _target.ClearValue(StateBindingTarget.ValueProperty);
    }
}

internal sealed class StateBindingTarget : AvaloniaObject
{
    internal static readonly object DisconnectedValue = new();

    internal static readonly StyledProperty<object?> ValueProperty =
        AvaloniaProperty.Register<StateBindingTarget, object?>(
            "Value",
            DisconnectedValue);

    private readonly Action _changed;

    public StateBindingTarget(Action changed)
    {
        _changed = changed;
    }

    internal object? Value => GetValue(ValueProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            _changed();
        }
    }
}
