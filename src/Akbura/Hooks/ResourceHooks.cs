using Akbura.CompilerAnotations;
using Akbura.ComponentTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Akbura.Hooks;

/// <summary>
/// Resource lookups scoped to a component's committed hook lifetime.
/// </summary>
public static class ResourceHooks
{
    private static readonly IUseHookDependenciesComparer s_dependencies = new ResourceDependenciesComparer();

    /// <summary>
    /// Resolves once after commit for this host/key/fallback identity. Ordinary
    /// renders and theme/resource notifications do not perform another lookup.
    /// A first miss on an unrooted host is retried once when the host attaches.
    /// </summary>
    [UseHook]
    public static State<T> useStaticResource<T>([Self] this AkburaControl control, object key, T fallback = default!) =>
        useStaticResource(control, control, key, fallback);

    [UseHook]
    public static State<T> useStaticResource<T>([Self] this AkburaControl control, IResourceHost host, object key, T fallback = default!)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(key);

        var result = control.useHookState(fallback);
        var cache = control.useHookState(static () => new StaticResourceCache<T>()).Value;
        control.useEffect(token =>
        {
            if (cache.Matches(host, key, fallback))
            {
                // Effects restart after reattachment, but a resolved static
                // value retains its original lookup result.
                result.Value = cache.Value;
                return null;
            }

            var found = TryRead(host, key, out var value);
            if (!found && host is ILogical logical && !logical.IsAttachedToLogicalTree)
            {
                result.Value = fallback;
                return new PendingStaticLookup<T>(logical, host, key, fallback, cache, result, token);
            }

            PublishStatic(host, key, fallback, value, cache, result, token);
            return null;
        }, [host, key, fallback], s_dependencies);
        return result;
    }

    /// <summary>
    /// Observes Avalonia resource and actual-theme changes. Subscribes only
    /// after a successful render; key/host changes replace the subscription.
    /// The first render returns fallback until that subscription publishes.
    /// Missing, null or incompatible resources produce fallback.
    /// </summary>
    [UseHook]
    public static State<T> useDynamicResource<T>(
        [Self] this AkburaControl control, object key, T fallback = default!) =>
        useDynamicResource(control, control, key, fallback);

    [UseHook]
    public static State<T> useDynamicResource<T>(
        [Self] this AkburaControl control, IResourceHost host, object key, T fallback = default!)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(key);

        var result = control.useHookState(fallback);
        control.useEffect((Func<CancellationToken, IDisposable?>)(token =>
            host.GetResourceObservable(key).Subscribe(new ResourceObserver<T>(result, fallback, token))),
            [host, key, fallback], s_dependencies);
        return result;
    }

    private static bool TryRead(IResourceHost host, object key, out object? value) =>
        host.TryFindResource(key, (host as IThemeVariantHost)?.ActualThemeVariant, out value);

    private static T ConvertValue<T>(object? value, T fallback)
    {
        if (value is null || ReferenceEquals(value, AvaloniaProperty.UnsetValue))
            return fallback;

        if (value is T typed)
            return typed;

        var converted = DefaultValueConverter.Instance.Convert(
            value,
            typeof(T),
            parameter: null,
            CultureInfo.CurrentCulture);

        if (converted is BindingNotification ||
            converted is null ||
            ReferenceEquals(converted, AvaloniaProperty.UnsetValue))
        {
            return fallback;
        }

        return converted is T result ? result : fallback;
    }

    private static void PublishStatic<T>(IResourceHost host, object key, T fallback, object? value,
        StaticResourceCache<T> cache, State<T> result, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        var converted = ConvertValue(value, fallback);
        cache.Set(host, key, fallback, converted);
        result.Value = converted;
    }

    private sealed class StaticResourceCache<T>
    {
        private bool _resolved;
        private IResourceHost? _host;
        private object? _key;
        private T _fallback = default!;
        public T Value { get; private set; } = default!;

        public bool Matches(IResourceHost host, object key, T fallback) => _resolved &&
            ReferenceEquals(_host, host) && Equals(_key, key) &&
            EqualityComparer<T>.Default.Equals(_fallback, fallback);

        public void Set(IResourceHost host, object key, T fallback, T value)
        {
            _host = host;
            _key = key;
            _fallback = fallback;
            Value = value;
            _resolved = true;
        }
    }

    private sealed class PendingStaticLookup<T> : IDisposable
    {
        private readonly ILogical _logical;
        private readonly IResourceHost _host;
        private readonly object _key;
        private readonly T _fallback;
        private readonly StaticResourceCache<T> _cache;
        private readonly State<T> _result;
        private readonly CancellationToken _token;
        private bool _disposed;

        public PendingStaticLookup(ILogical logical, IResourceHost host, object key, T fallback,
            StaticResourceCache<T> cache, State<T> result, CancellationToken token)
        {
            _logical = logical;
            _host = host;
            _key = key;
            _fallback = fallback;
            _cache = cache;
            _result = result;
            _token = token;
            logical.AttachedToLogicalTree += OnAttached;
            // Close the read/subscribe gap without polling on every render.
            if (logical.IsAttachedToLogicalTree) Resolve();
        }

        private void OnAttached(object? sender, LogicalTreeAttachmentEventArgs args) => Resolve();

        private void Resolve()
        {
            if (_disposed) return;
            Dispose();
            SourceHookDelivery.Dispatch(() =>
            {
                TryRead(_host, _key, out var value);
                PublishStatic(_host, _key, _fallback, value, _cache, _result, _token);
            }, _token);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _logical.AttachedToLogicalTree -= OnAttached;
        }
    }

    private sealed class ResourceObserver<T>(State<T> state, T fallback, CancellationToken token)
        : IObserver<object?>
    {
        public void OnNext(object? value) => SourceHookDelivery.Dispatch(
            () => { state.Value = ConvertValue(value, fallback); }, token);
        public void OnError(Exception error) => SourceHookDelivery.Dispatch(
            () => ExceptionDispatchInfo.Capture(error).Throw(), token);
        public void OnCompleted() { }
    }

    private sealed class ResourceDependenciesComparer : IUseHookDependenciesComparer
    {
        public bool Equals(ReadOnlySpan<object?> previousDependencies, ReadOnlySpan<object?> currentDependencies) =>
            previousDependencies.Length == 3 && currentDependencies.Length == 3 &&
            ReferenceEquals(previousDependencies[0], currentDependencies[0]) &&
            object.Equals(previousDependencies[1], currentDependencies[1]) &&
            object.Equals(previousDependencies[2], currentDependencies[2]);
    }
}
