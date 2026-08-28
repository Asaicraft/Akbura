using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;

using System;

namespace Akbura.Markup;

internal static class TailwindObservableProjection
{
    public static IObservable<bool> Create<T>(
        IObservable<T> source,
        Func<T, bool> selector)
    {
        return new ProjectionObservable<T>(source, selector);
    }

    private sealed class ProjectionObservable<T> : IObservable<bool>
    {
        private readonly IObservable<T> _source;
        private readonly Func<T, bool> _selector;

        public ProjectionObservable(
            IObservable<T> source,
            Func<T, bool> selector)
        {
            _source = source;
            _selector = selector;
        }

        public IDisposable Subscribe(IObserver<bool> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            return _source.Subscribe(
                new ProjectionObserver(observer, _selector));
        }

        private sealed class ProjectionObserver : IObserver<T>
        {
            private readonly IObserver<bool> _observer;
            private readonly Func<T, bool> _selector;

            private bool _hasValue;
            private bool _lastValue;
            private bool _isStopped;

            public ProjectionObserver(
                IObserver<bool> observer,
                Func<T, bool> selector)
            {
                _observer = observer;
                _selector = selector;
            }

            public void OnNext(T value)
            {
                if (_isStopped)
                {
                    return;
                }

                bool currentValue;
                try
                {
                    currentValue = _selector(value);
                }
                catch (Exception exception)
                {
                    _isStopped = true;
                    _observer.OnError(exception);
                    return;
                }

                if (_hasValue && currentValue == _lastValue)
                {
                    return;
                }

                _hasValue = true;
                _lastValue = currentValue;
                _observer.OnNext(currentValue);
            }

            public void OnError(Exception error)
            {
                if (_isStopped)
                {
                    return;
                }

                _isStopped = true;
                _observer.OnError(error);
            }

            public void OnCompleted()
            {
                if (_isStopped)
                {
                    return;
                }

                _isStopped = true;
                _observer.OnCompleted();
            }
        }
    }
}

/// <summary>
/// Provides a reactive AKCSS utility condition based on the target window's
/// client size.
/// </summary>
[UtilityBindingPriority(Priority = BindingPriority.StyleTrigger)]
public abstract class ViewportMarkupExtension
{
    /// <summary>
    /// Creates an observable viewport condition for the utility target.
    /// </summary>
    public IObservable<bool>? ProvideValue(IServiceProvider? serviceProvider)
    {
        if (serviceProvider?.GetService(typeof(IProvideValueTarget))
                is not IProvideValueTarget provideValueTarget ||
            provideValueTarget.TargetObject is not Visual target)
        {
            return null;
        }

        var topLevel = ResolveTopLevel(target, serviceProvider);
        if (topLevel is null)
        {
            return null;
        }

        return TailwindObservableProjection.Create(
            topLevel.GetObservable(TopLevel.ClientSizeProperty),
            IsActive);
    }

    /// <summary>Evaluates the current viewport size.</summary>
    protected abstract bool IsActive(Size size);

    internal static TopLevel? ResolveTopLevel(
        Visual target,
        IServiceProvider serviceProvider)
    {
        var topLevel = TopLevel.GetTopLevel(target);
        if (topLevel is not null)
        {
            return topLevel;
        }

        if (serviceProvider.GetService(typeof(IRootObjectProvider))
                is not IRootObjectProvider rootObjectProvider)
        {
            return null;
        }

        return rootObjectProvider.IntermediateRootObject as TopLevel
            ?? rootObjectProvider.RootObject as TopLevel;
    }
}

#pragma warning disable IDE1006 // Names intentionally match AKCSS prefixes.

/// <summary>Activates for left-to-right flow direction.</summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.Direction",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class ltrExtension : StyledElementStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool>? Observe(StyledElement target)
    {
        if (target is not Visual visual)
        {
            return null;
        }

        return TailwindObservableProjection.Create(
            visual.GetObservable(Visual.FlowDirectionProperty),
            static direction => direction == FlowDirection.LeftToRight);
    }
}

/// <summary>Activates for right-to-left flow direction.</summary>
[UtilityVariant(
    20d,
    ConflictGroup = "Akbura.Tailwind.Direction",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class rtlExtension : StyledElementStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool>? Observe(StyledElement target)
    {
        if (target is not Visual visual)
        {
            return null;
        }

        return TailwindObservableProjection.Create(
            visual.GetObservable(Visual.FlowDirectionProperty),
            static direction => direction == FlowDirection.RightToLeft);
    }
}

/// <summary>Activates when the viewport is portrait-oriented.</summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.Orientation",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class portraitExtension : ViewportMarkupExtension
{
    /// <inheritdoc />
    protected override bool IsActive(Size size)
    {
        return size.Height >= size.Width;
    }
}

/// <summary>Activates when the viewport is landscape-oriented.</summary>
[UtilityVariant(
    20d,
    ConflictGroup = "Akbura.Tailwind.Orientation",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class landscapeExtension : ViewportMarkupExtension
{
    /// <inheritdoc />
    protected override bool IsActive(Size size)
    {
        return size.Width > size.Height;
    }
}

/// <summary>
/// Activates when the platform requests high-contrast presentation.
/// Tailwind name: <c>contrast-more</c>.
/// </summary>
[UtilityBindingPriority(Priority = BindingPriority.StyleTrigger)]
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.Contrast",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class contrastMoreExtension
{
    /// <summary>Creates the reactive platform-contrast condition.</summary>
    public IObservable<bool>? ProvideValue(IServiceProvider? serviceProvider)
    {
        if (serviceProvider?.GetService(typeof(IProvideValueTarget))
                is not IProvideValueTarget provideValueTarget ||
            provideValueTarget.TargetObject is not Visual target)
        {
            return null;
        }

        var topLevel = ViewportMarkupExtension.ResolveTopLevel(
            target,
            serviceProvider);
        var platformSettings = topLevel?.GetPlatformSettings();
        return platformSettings is null
            ? null
            : new ContrastObservable(platformSettings);
    }

    private sealed class ContrastObservable : IObservable<bool>
    {
        private readonly IPlatformSettings _platformSettings;

        public ContrastObservable(IPlatformSettings platformSettings)
        {
            _platformSettings = platformSettings;
        }

        public IDisposable Subscribe(IObserver<bool> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            return new Subscription(_platformSettings, observer);
        }

        private sealed class Subscription : IDisposable
        {
            private IPlatformSettings? _platformSettings;
            private IObserver<bool>? _observer;

            private bool _hasValue;
            private bool _lastValue;

            public Subscription(
                IPlatformSettings platformSettings,
                IObserver<bool> observer)
            {
                _platformSettings = platformSettings;
                _observer = observer;

                platformSettings.ColorValuesChanged += OnColorValuesChanged;

                try
                {
                    Publish(platformSettings.GetColorValues());
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                var platformSettings = _platformSettings;
                if (platformSettings is null)
                {
                    return;
                }

                _platformSettings = null;
                _observer = null;
                platformSettings.ColorValuesChanged -= OnColorValuesChanged;
            }

            private void OnColorValuesChanged(
                object? sender,
                PlatformColorValues colorValues)
            {
                Publish(colorValues);
            }

            private void Publish(PlatformColorValues colorValues)
            {
                var observer = _observer;
                if (_platformSettings is null || observer is null)
                {
                    return;
                }

                var currentValue = colorValues.ContrastPreference ==
                    ColorContrastPreference.High;
                if (_hasValue && currentValue == _lastValue)
                {
                    return;
                }

                _hasValue = true;
                _lastValue = currentValue;
                observer.OnNext(currentValue);
            }
        }
    }
}

/// <summary>
/// Activates at an arbitrary minimum viewport width.
/// Use as <c>${min Width=900}:...</c>.
/// </summary>
[UtilityVariant(
    0d,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class minExtension : ViewportMarkupExtension
{
    /// <summary>Gets or sets the minimum width in device-independent pixels.</summary>
    public double Width { get; set; } = double.NaN;

    /// <inheritdoc />
    protected override bool IsActive(Size size)
    {
        return !double.IsNaN(Width) && size.Width >= Width;
    }
}

/// <summary>
/// Activates below an arbitrary maximum viewport width.
/// Use as <c>${max Width=900}:...</c>.
/// </summary>
[UtilityVariant(
    0d,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class maxExtension : ViewportMarkupExtension
{
    /// <summary>Gets or sets the exclusive maximum width.</summary>
    public double Width { get; set; } = double.NaN;

    /// <inheritdoc />
    protected override bool IsActive(Size size)
    {
        return !double.IsNaN(Width) && size.Width < Width;
    }
}

#pragma warning restore IDE1006 // Names intentionally match AKCSS prefixes.




