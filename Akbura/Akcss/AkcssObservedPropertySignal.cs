using Akbura.CompilerAnotations;
using Avalonia;
using System.Collections.Concurrent;
using System.Reflection;

namespace Akbura.Akcss;

internal static class AkcssObservedPropertySignal
{
    private static readonly ConcurrentDictionary<Type, string[]> s_propertyNames = new();

    public static IObservable<object?> Create(Type styleType, object target)
    {
        ArgumentNullException.ThrowIfNull(styleType);
        ArgumentNullException.ThrowIfNull(target);

        var propertyNames = s_propertyNames.GetOrAdd(styleType, GetPropertyNames);
        if (propertyNames.Length == 0)
        {
            return EmptySignal.Instance;
        }

        if (target is not AvaloniaObject avaloniaObject)
        {
            throw new InvalidOperationException(
                $"AKCSS style '{styleType.FullName}' observes Avalonia properties, " +
                $"but target '{target.GetType().FullName}' is not an AvaloniaObject.");
        }

        var properties = new List<AvaloniaProperty>(propertyNames.Length);
        foreach (var propertyName in propertyNames)
        {
            var property = FindProperty(avaloniaObject, propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"AKCSS style '{styleType.FullName}' observes property '{propertyName}', " +
                    $"which is not registered on '{target.GetType().FullName}'.");
            }

            if (!properties.Contains(property))
            {
                properties.Add(property);
            }
        }

        return new ObservedPropertiesSignal(avaloniaObject, properties.ToArray());
    }

    private static string[] GetPropertyNames(Type styleType)
    {
        var attributes = styleType.GetCustomAttributes<ObservesPropertyAttribute>(inherit: true);
        var names = new List<string>();
        foreach (var attribute in attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.PropertyName))
            {
                throw new InvalidOperationException(
                    $"AKCSS style '{styleType.FullName}' has an ObservesProperty attribute " +
                    "without a property name.");
            }

            if (!names.Contains(attribute.PropertyName, StringComparer.Ordinal))
            {
                names.Add(attribute.PropertyName);
            }
        }

        return names.ToArray();
    }

    private static AvaloniaProperty? FindProperty(AvaloniaObject target, string propertyName)
    {
        var registry = AvaloniaPropertyRegistry.Instance;
        var property = registry.FindRegistered(target, propertyName);
        if (property != null || !propertyName.EndsWith("Property", StringComparison.Ordinal))
        {
            return property;
        }

        return registry.FindRegistered(
            target,
            propertyName[..^"Property".Length]);
    }

    private sealed class ObservedPropertiesSignal : IObservable<object?>
    {
        private readonly AvaloniaObject _target;
        private readonly AvaloniaProperty[] _properties;

        public ObservedPropertiesSignal(
            AvaloniaObject target,
            AvaloniaProperty[] properties)
        {
            _target = target;
            _properties = properties;
        }

        public IDisposable Subscribe(IObserver<object?> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            var subscriptions = new IDisposable[_properties.Length];
            var count = 0;
            try
            {
                for (; count < _properties.Length; count++)
                {
                    subscriptions[count] = _target
                        .GetObservable(_properties[count])
                        .Subscribe(new PropertyObserver(observer));
                }

                return new CompositeDisposable(subscriptions);
            }
            catch
            {
                for (var index = 0; index < count; index++)
                {
                    subscriptions[index].Dispose();
                }

                throw;
            }
        }
    }

    private sealed class PropertyObserver : IObserver<object?>
    {
        private readonly IObserver<object?> _observer;

        public PropertyObserver(IObserver<object?> observer)
        {
            _observer = observer;
        }

        public void OnNext(object? value)
        {
            _observer.OnNext(AvaloniaProperty.UnsetValue);
        }

        public void OnError(Exception error)
        {
            _observer.OnError(error);
        }

        public void OnCompleted()
        {
        }
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private IDisposable[]? _subscriptions;

        public CompositeDisposable(IDisposable[] subscriptions)
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

            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }
    }

    private sealed class EmptySignal : IObservable<object?>
    {
        public static readonly EmptySignal Instance = new();

        public IDisposable Subscribe(IObserver<object?> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            return EmptyDisposable.Instance;
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
