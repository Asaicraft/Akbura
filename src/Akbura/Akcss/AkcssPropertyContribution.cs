using Akbura.Markup;
using Avalonia;
using Avalonia.Data;
using System.ComponentModel;
using System.Runtime.ExceptionServices;

namespace Akbura.Akcss;

/// <summary>
/// Owns one reversible, non-local Avalonia property source independently of generated IL.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkcssPropertyContribution : IDisposable, IObservable<object?>
{
    private readonly AvaloniaObject _target;
    private readonly AvaloniaProperty _property;
    private readonly BindingPriority _priority;
    private readonly IObservable<object?>? _source;
    private IObserver<object?>? _bindingObserver;
    private SourceObserver? _sourceObserver;
    private IDisposable? _binding;
    private IDisposable? _subscription;
    private object? _value = AvaloniaProperty.UnsetValue;
    private bool _active;
    private bool _disposed;

    private AkcssPropertyContribution(AvaloniaObject target, AvaloniaProperty property,
        BindingPriority priority, IObservable<object?>? source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        target.VerifyAccess();
        if (property.IsDirect)
        {
            throw new NotSupportedException("Direct properties do not support owned AKCSS contributions.");
        }
        _target = target;
        _property = property;
        _priority = AkcssBindingPriority.Validate(priority);
        _source = source;
    }

    /// <summary>
    /// Installs a scalar source without overwriting any external local value or binding.
    /// </summary>
    public static IDisposable SetValue(AvaloniaObject target, AvaloniaProperty property, object? value, BindingPriority priority)
    {
        var result = new AkcssPropertyContribution(target, property, priority, null) { _value = value };
        result.Validate(value);
        return result.Install();
    }

    /// <summary>
    /// Installs a resource source whose subscription can survive a suspended transaction.
    /// </summary>
    public static IDisposable Bind(AvaloniaObject target, AvaloniaProperty property,
        IObservable<object?> source, BindingPriority priority)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AkcssPropertyContribution(target, property, priority, source).Install();
    }

    internal bool IsActive => _active && !_disposed;

    private void Validate(object? value)
    {
        if (!ReferenceEquals(value, AvaloniaProperty.UnsetValue) && !_property.IsValidValue(value))
        {
            throw new ArgumentException($"The value is invalid for AKCSS property '{_property.Name}'.", nameof(value));
        }
    }

    internal void PublishLatest()
    {
        if (IsActive)
        {
            _bindingObserver?.OnNext(_value);
        }
    }

    private AkcssPropertyContribution Install()
    {
        try
        {
            // Bind while inactive. A notification from the first real value may
            // throw; the handle must already be owned before that notification.
            _binding = _target.Bind(_property, this, _priority);
            if (_source != null)
            {
                _sourceObserver = new SourceObserver(this);
                _subscription = _source.Subscribe(_sourceObserver);
            }
            Resume();
            return this;
        }
        catch (Exception failure)
        {
            try
            {
                Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("AKCSS contribution installation and cleanup failed.", failure, cleanup);
            }
            throw;
        }
    }

    internal void Suspend()
    {
        _target.VerifyAccess();
        _active = false;
        _bindingObserver?.OnNext(AvaloniaProperty.UnsetValue);
    }

    internal void Resume()
    {
        _target.VerifyAccess();
        if (_disposed)
        {
            _disposed = false;
            Install();
            return;
        }
        _active = true;
        _bindingObserver?.OnNext(_value);
    }

    IDisposable IObservable<object?>.Subscribe(IObserver<object?> observer)
    {
        _bindingObserver = observer;
        observer.OnNext(_active ? _value : AvaloniaProperty.UnsetValue);
        return new BindingSubscription(this, observer);
    }

    /// <summary>
    /// Releases only this property's binding and source; repeated release is harmless.
    /// </summary>
    public void Dispose()
    {
        _target.VerifyAccess();
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _active = false;
        _sourceObserver?.Deactivate();
        _sourceObserver = null;
        var subscription = _subscription;
        var binding = _binding;
        _subscription = _binding = null;
        List<Exception>? failures = null;
        TryDispose(subscription);
        TryDispose(binding);
        _bindingObserver = null;
        if (failures?.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures != null)
        {
            throw new AggregateException("AKCSS contribution cleanup failed.", failures);
        }

        void TryDispose(IDisposable? resource)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }
    }

    private sealed class BindingSubscription(AkcssPropertyContribution owner, IObserver<object?> observer) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(owner._bindingObserver, observer))
            {
                owner._bindingObserver = null;
            }
        }
    }

    private sealed class SourceObserver(AkcssPropertyContribution owner) : IObserver<object?>
    {
        private AkcssPropertyContribution? _owner = owner;
        public void Deactivate() => _owner = null;
        public void OnNext(object? value)
        {
            if (_owner is not { } current)
            {
                return;
            }
            current._target.VerifyAccess();
            current.Validate(value);
            current._value = value;
            if (current.IsActive && !AkcssRuntime.QueueContributionUpdate(current._target, current))
            {
                current.PublishLatest();
            }
        }
        public void OnError(Exception error)
        {
            if (_owner != null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
        public void OnCompleted()
        {
        }
    }
}
