using Avalonia;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using System;

namespace Akbura.Markup;

using static TailwindVariantGroupKeys;

file static class TailwindVariantGroupKeys
{
    public const string Interaction = "Akbura.Tailwind.Interaction";

    public const string ColorScheme = "Akbura.Tailwind.ColorScheme";
}

/// <summary>
/// Provides a reactive AKCSS utility condition based on an
/// <see cref="InputElement"/> state.
/// </summary>
[UtilityBindingPriority(Priority = BindingPriority.StyleTrigger)]
public abstract class InputStateMarkupExtension
{
    /// <summary>
    /// Creates an observable condition for the target input element.
    /// </summary>
    /// <param name="serviceProvider">
    /// The markup service provider for the utility target.
    /// </param>
    /// <returns>
    /// The state observable, or <see langword="null"/> when no input-element
    /// target is available.
    /// </returns>
    public IObservable<bool>? ProvideValue(IServiceProvider? serviceProvider)
    {
        if (serviceProvider?.GetService(typeof(IProvideValueTarget))
                is not IProvideValueTarget provideValueTarget ||
            provideValueTarget.TargetObject is not InputElement target)
        {
            return null;
        }

        return Observe(target);
    }

    /// <summary>
    /// Observes the state represented by this extension.
    /// </summary>
    /// <param name="target">The utility target.</param>
    /// <returns>The state observable.</returns>
    protected abstract IObservable<bool> Observe(InputElement target);
}

/// <summary>
/// Provides a reactive AKCSS utility condition based on the effective
/// Avalonia theme variant of the target element.
/// </summary>
[UtilityBindingPriority(Priority = BindingPriority.StyleTrigger)]
public abstract class ThemeVariantMarkupExtension
{
    private readonly ThemeVariant _themeVariant;

    /// <summary>
    /// Initializes a new instance for the requested theme variant.
    /// </summary>
    /// <param name="themeVariant">The theme that activates this extension.</param>
    protected ThemeVariantMarkupExtension(ThemeVariant themeVariant)
    {
        _themeVariant = themeVariant ??
            throw new ArgumentNullException(nameof(themeVariant));
    }

    /// <summary>
    /// Creates an observable condition for the target styled element.
    /// </summary>
    /// <param name="serviceProvider">
    /// The markup service provider for the utility target.
    /// </param>
    /// <returns>
    /// An observable that is true while the requested theme is effective, or
    /// <see langword="null"/> when no styled-element target is available.
    /// </returns>
    public IObservable<bool>? ProvideValue(IServiceProvider? serviceProvider)
    {
        if (serviceProvider?.GetService(typeof(IProvideValueTarget))
                is not IProvideValueTarget provideValueTarget ||
            provideValueTarget.TargetObject is not StyledElement target)
        {
            return null;
        }

        return new ThemeVariantConditionObservable(target, _themeVariant);
    }

    private sealed class ThemeVariantConditionObservable : IObservable<bool>
    {
        private readonly StyledElement _target;
        private readonly ThemeVariant _themeVariant;

        public ThemeVariantConditionObservable(
            StyledElement target,
            ThemeVariant themeVariant)
        {
            _target = target;
            _themeVariant = themeVariant;
        }

        public IDisposable Subscribe(IObserver<bool> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            return new ThemeVariantConditionSubscription(
                _target,
                _themeVariant,
                observer);
        }
    }

    private sealed class ThemeVariantConditionSubscription : IDisposable
    {
        private StyledElement? _target;
        private IObserver<bool>? _observer;
        private readonly ThemeVariant _themeVariant;

        private bool _hasValue;
        private bool _lastValue;

        public ThemeVariantConditionSubscription(
            StyledElement target,
            ThemeVariant themeVariant,
            IObserver<bool> observer)
        {
            _target = target;
            _themeVariant = themeVariant;
            _observer = observer;

            target.ActualThemeVariantChanged += OnActualThemeVariantChanged;

            try
            {
                PublishCurrentValue();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            var target = _target;
            if (target is null)
            {
                return;
            }

            _target = null;
            _observer = null;
            target.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }

        private void OnActualThemeVariantChanged(
            object? sender,
            EventArgs eventArgs)
        {
            PublishCurrentValue();
        }

        private void PublishCurrentValue()
        {
            var target = _target;
            var observer = _observer;
            if (target is null || observer is null)
            {
                return;
            }

            var currentValue = MatchesTheme(target.ActualThemeVariant);
            if (_hasValue && currentValue == _lastValue)
            {
                return;
            }

            _hasValue = true;
            _lastValue = currentValue;
            observer.OnNext(currentValue);
        }

        private bool MatchesTheme(ThemeVariant actualThemeVariant)
        {
            ThemeVariant? current = actualThemeVariant;
            while (current is not null)
            {
                if (_themeVariant.Equals(current))
                {
                    return true;
                }

                current = current.InheritVariant;
            }

            return false;
        }
    }
}

#pragma warning disable IDE1006 // Markup extension names intentionally match Tailwind variants.

/// <summary>
/// Activates an AKCSS utility while the target is pointer-over.
/// </summary>
[UtilityVariant(
    10d,
    ConflictGroup = Interaction,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class hoverExtension : InputStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool> Observe(InputElement target)
    {
        return target.GetObservable(InputElement.IsPointerOverProperty);
    }
}

/// <summary>
/// Activates an AKCSS utility while the target itself is focused.
/// </summary>
[UtilityVariant(
    20d,
    ConflictGroup = Interaction,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class focusExtension : InputStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool> Observe(InputElement target)
    {
        return target.GetObservable(InputElement.IsFocusedProperty);
    }
}

/// <summary>
/// Activates an AKCSS utility while the target uses the dark theme variant.
/// </summary>
[UtilityVariant(
    20d,
    ConflictGroup = ColorScheme,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class darkExtension : ThemeVariantMarkupExtension
{
    /// <summary>
    /// Initializes a new instance of the dark-theme utility variant.
    /// </summary>
    public darkExtension()
        : base(ThemeVariant.Dark)
    {
    }
}

/// <summary>
/// Activates an AKCSS utility while the target uses the light theme variant.
/// </summary>
[UtilityVariant(
    10d,
    ConflictGroup = ColorScheme,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class lightExtension : ThemeVariantMarkupExtension
{
    /// <summary>
    /// Initializes a new instance of the light-theme utility variant.
    /// </summary>
    public lightExtension()
        : base(ThemeVariant.Light)
    {
    }
}

#pragma warning restore IDE1006 // Markup extension names intentionally match Tailwind variants.
