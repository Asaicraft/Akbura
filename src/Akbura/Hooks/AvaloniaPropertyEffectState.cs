using Avalonia;
using System.Runtime.ExceptionServices;

namespace Akbura.Hooks;

internal readonly struct AvaloniaPropertyEffectArguments
{
    public AvaloniaPropertyEffectArguments(
        UseHookKey key,
        UseEffectCallback callback,
        AvaloniaObject target,
        ReadOnlySpan<AvaloniaProperty> properties)
    {
        Key = key;
        Callback = callback ?? throw new ArgumentNullException(nameof(callback));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Properties = properties.ToArray();

        for (var index = 0; index < Properties.Length; index++)
        {
            if (Properties[index] == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }
        }
    }

    public UseHookKey Key { get; }

    public UseEffectCallback Callback { get; }

    public AvaloniaObject Target { get; }

    public AvaloniaProperty[] Properties { get; }
}

internal sealed class AvaloniaPropertyEffectState
{
    private readonly UseEffectSlot _effect;
    private AvaloniaObject? _target;
    private AvaloniaProperty[]? _properties;
    private List<IDisposable>? _subscriptions;
    private int _sourceVersion;

    public AvaloniaPropertyEffectState(UseHookKey key)
    {
        _effect = new UseEffectSlot(key);
    }

    public void Apply(AvaloniaPropertyEffectArguments arguments)
    {
        if (_subscriptions == null || !HasSameSources(arguments))
        {
            DisposeSubscriptions();
            _target = arguments.Target;
            _properties = arguments.Properties;
            _sourceVersion++;
            Subscribe();
        }

        _effect.Apply(new UseEffectRegistration(
            arguments.Key,
            arguments.Callback,
            hasDependencies: true,
            dependencies: [_sourceVersion],
            comparer: null));
    }

    public void StopForDetach()
    {
        List<Exception>? failures = null;
        try
        {
            _effect.StopForDetach();
        }
        catch (Exception exception)
        {
            UseHookFailures.Capture(ref failures, exception);
        }

        try
        {
            DisposeSubscriptions();
        }
        catch (Exception exception)
        {
            UseHookFailures.Capture(ref failures, exception);
        }

        UseHookFailures.ThrowIfAny(
            failures,
            "An Avalonia property effect could not be stopped.");
    }

    private bool HasSameSources(AvaloniaPropertyEffectArguments arguments)
    {
        if (!ReferenceEquals(_target, arguments.Target))
        {
            return false;
        }

        var previous = _properties ?? Array.Empty<AvaloniaProperty>();
        if (previous.Length != arguments.Properties.Length)
        {
            return false;
        }

        for (var index = 0; index < previous.Length; index++)
        {
            if (!ReferenceEquals(previous[index], arguments.Properties[index]))
            {
                return false;
            }
        }

        return true;
    }

    private void Subscribe()
    {
        if (_target == null || _properties == null)
        {
            return;
        }

        _subscriptions = new List<IDisposable>(_properties.Length);
        for (var index = 0; index < _properties.Length; index++)
        {
            var property = _properties[index];
            if (ContainsPropertyBefore(index, property))
            {
                continue;
            }

            var initialValue = _target.GetValue(property);
            var subscription = _target
                .GetObservable(property)
                .Subscribe(new PropertyObserver(this, initialValue));
            _subscriptions.Add(subscription);
        }
    }

    private bool ContainsPropertyBefore(int index, AvaloniaProperty property)
    {
        for (var previousIndex = 0; previousIndex < index; previousIndex++)
        {
            if (ReferenceEquals(_properties![previousIndex], property))
            {
                return true;
            }
        }

        return false;
    }

    private void DisposeSubscriptions()
    {
        var subscriptions = _subscriptions;
        _subscriptions = null;
        if (subscriptions == null)
        {
            return;
        }

        List<Exception>? failures = null;
        foreach (var subscription in subscriptions)
        {
            try
            {
                subscription.Dispose();
            }
            catch (Exception exception)
            {
                UseHookFailures.Capture(ref failures, exception);
            }
        }

        UseHookFailures.ThrowIfAny(
            failures,
            "One or more Avalonia property subscriptions could not be disposed.");
    }

    private sealed class PropertyObserver : IObserver<object?>
    {
        private readonly AvaloniaPropertyEffectState _state;
        private readonly object? _initialValue;
        private bool _isFirstValue = true;

        public PropertyObserver(
            AvaloniaPropertyEffectState state,
            object? initialValue)
        {
            _state = state;
            _initialValue = initialValue;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        public void OnNext(object? value)
        {
            if (_isFirstValue)
            {
                _isFirstValue = false;
                if (EqualityComparer<object?>.Default.Equals(_initialValue, value))
                {
                    return;
                }
            }

            _state._effect.Trigger();
        }
    }
}
