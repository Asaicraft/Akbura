using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using System;
using System.Collections.Specialized;

namespace Akbura.Markup;

/// <summary>
/// Provides a reactive AKCSS utility condition based on a styled-element
/// state.
/// </summary>
[UtilityBindingPriority(Priority = BindingPriority.StyleTrigger)]
public abstract class StyledElementStateMarkupExtension
{
    /// <summary>
    /// Creates an observable condition for the utility target.
    /// </summary>
    /// <param name="serviceProvider">The markup service provider.</param>
    /// <returns>
    /// The state observable, or <see langword="null"/> when the target does
    /// not support the requested state.
    /// </returns>
    public IObservable<bool>? ProvideValue(IServiceProvider? serviceProvider)
    {
        if (serviceProvider?.GetService(typeof(IProvideValueTarget))
                is not IProvideValueTarget provideValueTarget ||
            provideValueTarget.TargetObject is not StyledElement target)
        {
            return null;
        }

        return Observe(target);
    }

    /// <summary>
    /// Creates the state observable for <paramref name="target"/>.
    /// </summary>
    protected abstract IObservable<bool>? Observe(StyledElement target);
}

/// <summary>
/// Provides a reactive condition backed by one or more Avalonia
/// pseudo-classes.
/// </summary>
public abstract class PseudoClassMarkupExtension
    : StyledElementStateMarkupExtension
{
    private readonly string[] _pseudoClasses;

    /// <summary>
    /// Initializes a condition that is active while the pseudo-class is set.
    /// </summary>
    protected PseudoClassMarkupExtension(string pseudoClass)
        : this([pseudoClass])
    {
    }

    /// <summary>
    /// Initializes a condition that is active while any supplied
    /// pseudo-class is set.
    /// </summary>
    protected PseudoClassMarkupExtension(params string[] pseudoClasses)
    {
        ArgumentNullException.ThrowIfNull(pseudoClasses);
        if (pseudoClasses.Length == 0)
        {
            throw new ArgumentException(
                "At least one pseudo-class is required.",
                nameof(pseudoClasses));
        }

        _pseudoClasses = new string[pseudoClasses.Length];
        for (var index = 0; index < pseudoClasses.Length; index++)
        {
            var pseudoClass = pseudoClasses[index];
            if (string.IsNullOrWhiteSpace(pseudoClass) ||
                pseudoClass[0] != ':')
            {
                throw new ArgumentException(
                    "Pseudo-class names must begin with ':'.",
                    nameof(pseudoClasses));
            }

            _pseudoClasses[index] = pseudoClass;
        }
    }

    /// <inheritdoc />
    protected sealed override IObservable<bool> Observe(StyledElement target)
    {
        return new PseudoClassObservable(target.Classes, _pseudoClasses);
    }

    private sealed class PseudoClassObservable : IObservable<bool>
    {
        private readonly Classes _classes;
        private readonly string[] _pseudoClasses;

        public PseudoClassObservable(
            Classes classes,
            string[] pseudoClasses)
        {
            _classes = classes;
            _pseudoClasses = pseudoClasses;
        }

        public IDisposable Subscribe(IObserver<bool> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            return new Subscription(_classes, _pseudoClasses, observer);
        }

        private sealed class Subscription : IDisposable
        {
            private Classes? _classes;
            private IObserver<bool>? _observer;
            private readonly string[] _pseudoClasses;

            private bool _hasValue;
            private bool _lastValue;

            public Subscription(
                Classes classes,
                string[] pseudoClasses,
                IObserver<bool> observer)
            {
                _classes = classes;
                _pseudoClasses = pseudoClasses;
                _observer = observer;

                classes.CollectionChanged += OnClassesChanged;

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
                var classes = _classes;
                if (classes is null)
                {
                    return;
                }

                _classes = null;
                _observer = null;
                classes.CollectionChanged -= OnClassesChanged;
            }

            private void OnClassesChanged(
                object? sender,
                NotifyCollectionChangedEventArgs eventArgs)
            {
                PublishCurrentValue();
            }

            private void PublishCurrentValue()
            {
                var classes = _classes;
                var observer = _observer;
                if (classes is null || observer is null)
                {
                    return;
                }

                var pseudoClasses = (IPseudoClasses)classes;
                var currentValue = false;
                foreach (var pseudoClass in _pseudoClasses)
                {
                    if (pseudoClasses.Contains(pseudoClass))
                    {
                        currentValue = true;
                        break;
                    }
                }

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

internal static class TailwindStateObservable
{
    public static IObservable<bool> Negate(IObservable<bool> source)
    {
        return new NegatedObservable(source);
    }

    public static IObservable<bool> Observe(
        AvaloniaObject target,
        AvaloniaProperty[] properties,
        Func<bool> evaluate)
    {
        return new PropertyConditionObservable(target, properties, evaluate);
    }

    private sealed class NegatedObservable : IObservable<bool>
    {
        private readonly IObservable<bool> _source;

        public NegatedObservable(IObservable<bool> source)
        {
            _source = source;
        }

        public IDisposable Subscribe(IObserver<bool> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            return _source.Subscribe(new NegatedObserver(observer));
        }

        private sealed class NegatedObserver : IObserver<bool>
        {
            private readonly IObserver<bool> _observer;

            public NegatedObserver(IObserver<bool> observer)
            {
                _observer = observer;
            }

            public void OnNext(bool value)
            {
                _observer.OnNext(!value);
            }

            public void OnError(Exception error)
            {
                _observer.OnError(error);
            }

            public void OnCompleted()
            {
                _observer.OnCompleted();
            }
        }
    }

    private sealed class PropertyConditionObservable : IObservable<bool>
    {
        private readonly AvaloniaObject _target;
        private readonly AvaloniaProperty[] _properties;
        private readonly Func<bool> _evaluate;

        public PropertyConditionObservable(
            AvaloniaObject target,
            AvaloniaProperty[] properties,
            Func<bool> evaluate)
        {
            _target = target;
            _properties = properties;
            _evaluate = evaluate;
        }

        public IDisposable Subscribe(IObserver<bool> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            return new Subscription(
                _target,
                _properties,
                _evaluate,
                observer);
        }

        private sealed class Subscription : IDisposable
        {
            private AvaloniaObject? _target;
            private IObserver<bool>? _observer;
            private readonly AvaloniaProperty[] _properties;
            private readonly Func<bool> _evaluate;

            private bool _hasValue;
            private bool _lastValue;

            public Subscription(
                AvaloniaObject target,
                AvaloniaProperty[] properties,
                Func<bool> evaluate,
                IObserver<bool> observer)
            {
                _target = target;
                _properties = properties;
                _evaluate = evaluate;
                _observer = observer;

                target.PropertyChanged += OnPropertyChanged;

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
                target.PropertyChanged -= OnPropertyChanged;
            }

            private void OnPropertyChanged(
                object? sender,
                AvaloniaPropertyChangedEventArgs eventArgs)
            {
                foreach (var property in _properties)
                {
                    if (eventArgs.Property == property)
                    {
                        PublishCurrentValue();
                        return;
                    }
                }
            }

            private void PublishCurrentValue()
            {
                var observer = _observer;
                if (_target is null || observer is null)
                {
                    return;
                }

                var currentValue = _evaluate();
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

#pragma warning disable IDE1006 // Names intentionally match AKCSS prefixes.

/// <summary>
/// Activates while the target or one of its descendants has keyboard focus.
/// Tailwind name: <c>focus-within</c>.
/// </summary>
[UtilityVariant(
    5d,
    ConflictGroup = "Akbura.Tailwind.Interaction",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class focusWithinExtension : InputStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool> Observe(InputElement target)
    {
        return target.GetObservable(InputElement.IsKeyboardFocusWithinProperty);
    }
}

/// <summary>
/// Activates while Avalonia exposes the keyboard-focus-visible state.
/// Tailwind name: <c>focus-visible</c>.
/// </summary>
[UtilityVariant(
    30d,
    ConflictGroup = "Akbura.Tailwind.Interaction",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class focusVisibleExtension : PseudoClassMarkupExtension
{
    /// <summary>Initializes the focus-visible variant.</summary>
    public focusVisibleExtension()
        : base(":focus-visible")
    {
    }
}

/// <summary>
/// Activates while a supported Avalonia control is pressed.
/// </summary>
[UtilityVariant(
    40d,
    ConflictGroup = "Akbura.Tailwind.Interaction",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class activeExtension : PseudoClassMarkupExtension
{
    /// <summary>Initializes the active variant.</summary>
    public activeExtension()
        : base(":pressed")
    {
    }
}

/// <summary>Activates while the target is effectively enabled.</summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.Availability",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class enabledExtension : InputStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool> Observe(InputElement target)
    {
        return target.GetObservable(InputElement.IsEffectivelyEnabledProperty);
    }
}

/// <summary>Activates while the target is effectively disabled.</summary>
[UtilityVariant(
    20d,
    ConflictGroup = "Akbura.Tailwind.Availability",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class disabledExtension : InputStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool> Observe(InputElement target)
    {
        return TailwindStateObservable.Negate(
            target.GetObservable(InputElement.IsEffectivelyEnabledProperty));
    }
}

/// <summary>Activates while a supported link is marked as visited.</summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.LinkState",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class visitedExtension : PseudoClassMarkupExtension
{
    /// <summary>Initializes the visited variant.</summary>
    public visitedExtension()
        : base(":visited")
    {
    }
}

/// <summary>
/// Activates while a disclosure, menu, flyout, or drop-down target is open.
/// </summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.Disclosure",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class openExtension : PseudoClassMarkupExtension
{
    /// <summary>Initializes the open variant.</summary>
    public openExtension()
        : base(":open", ":expanded", ":dropdownopen", ":flyout-open")
    {
    }
}

/// <summary>Activates while a toggle target is checked.</summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.ToggleState",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class checkedExtension : PseudoClassMarkupExtension
{
    /// <summary>Initializes the checked variant.</summary>
    public checkedExtension()
        : base(":checked")
    {
    }
}

/// <summary>Activates while a three-state target is indeterminate.</summary>
[UtilityVariant(
    20d,
    ConflictGroup = "Akbura.Tailwind.ToggleState",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class indeterminateExtension : PseudoClassMarkupExtension
{
    /// <summary>Initializes the indeterminate variant.</summary>
    public indeterminateExtension()
        : base(":indeterminate")
    {
    }
}

/// <summary>Activates while an Avalonia item container is selected.</summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.Selection",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class selectedExtension : PseudoClassMarkupExtension
{
    /// <summary>Initializes the selected variant.</summary>
    public selectedExtension()
        : base(":selected")
    {
    }
}

/// <summary>
/// Activates while the target is marked as a required form control.
/// </summary>
[UtilityVariant(
    20d,
    ConflictGroup = "Akbura.Tailwind.Requirement",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class requiredExtension : StyledElementStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool> Observe(StyledElement target)
    {
        return target.GetObservable(
            AutomationProperties.IsRequiredForFormProperty);
    }
}

/// <summary>
/// Activates while the target is not marked as a required form control.
/// </summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.Requirement",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class optionalExtension : StyledElementStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool> Observe(StyledElement target)
    {
        return TailwindStateObservable.Negate(
            target.GetObservable(
                AutomationProperties.IsRequiredForFormProperty));
    }
}

/// <summary>
/// Activates while Avalonia data validation reports no errors.
/// </summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.Validation",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class validExtension : StyledElementStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool> Observe(StyledElement target)
    {
        return TailwindStateObservable.Negate(
            target.GetObservable(DataValidationErrors.HasErrorsProperty));
    }
}

/// <summary>
/// Activates while Avalonia data validation reports one or more errors.
/// </summary>
[UtilityVariant(
    20d,
    ConflictGroup = "Akbura.Tailwind.Validation",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class invalidExtension : StyledElementStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool> Observe(StyledElement target)
    {
        return target.GetObservable(DataValidationErrors.HasErrorsProperty);
    }
}

/// <summary>
/// Activates while a numeric editor has a non-null value inside its declared
/// inclusive range. Tailwind name: <c>in-range</c>.
/// </summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.RangeState",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class inRangeExtension : StyledElementStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool>? Observe(StyledElement target)
    {
        if (target is not NumericUpDown numericUpDown)
        {
            return null;
        }

        return TailwindStateObservable.Observe(
            numericUpDown,
            [
                NumericUpDown.ValueProperty,
                NumericUpDown.MinimumProperty,
                NumericUpDown.MaximumProperty,
            ],
            () =>
                numericUpDown.Value is decimal value &&
                value >= numericUpDown.Minimum &&
                value <= numericUpDown.Maximum);
    }
}

/// <summary>
/// Activates while a numeric editor has a non-null value outside its declared
/// range. Tailwind name: <c>out-of-range</c>.
/// </summary>
[UtilityVariant(
    20d,
    ConflictGroup = "Akbura.Tailwind.RangeState",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class outOfRangeExtension : StyledElementStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool>? Observe(StyledElement target)
    {
        if (target is not NumericUpDown numericUpDown)
        {
            return null;
        }

        return TailwindStateObservable.Observe(
            numericUpDown,
            [
                NumericUpDown.ValueProperty,
                NumericUpDown.MinimumProperty,
                NumericUpDown.MaximumProperty,
            ],
            () =>
                numericUpDown.Value is decimal value &&
                (value < numericUpDown.Minimum ||
                 value > numericUpDown.Maximum));
    }
}

/// <summary>
/// Activates while a supported text or numeric editor is read-only.
/// Tailwind name: <c>read-only</c>.
/// </summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.Editability",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class readOnlyExtension : StyledElementStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool>? Observe(StyledElement target)
    {
        return target switch
        {
            TextBox textBox =>
                textBox.GetObservable(TextBox.IsReadOnlyProperty),
            NumericUpDown numericUpDown =>
                numericUpDown.GetObservable(NumericUpDown.IsReadOnlyProperty),
            _ => null,
        };
    }
}

/// <summary>
/// Activates while an empty text box can display placeholder text.
/// Tailwind name: <c>placeholder-shown</c>.
/// </summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.Placeholder",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class placeholderShownExtension
    : StyledElementStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool>? Observe(StyledElement target)
    {
        if (target is not TextBox textBox)
        {
            return null;
        }

        return TailwindStateObservable.Observe(
            textBox,
            [
                TextBox.TextProperty,
                TextBox.PlaceholderTextProperty,
            ],
            () =>
                string.IsNullOrEmpty(textBox.Text) &&
                !string.IsNullOrEmpty(textBox.PlaceholderText));
    }
}

/// <summary>
/// Activates while a button is configured as the default action.
/// </summary>
[UtilityVariant(
    10d,
    ConflictGroup = "Akbura.Tailwind.DefaultState",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class defaultExtension : StyledElementStateMarkupExtension
{
    /// <inheritdoc />
    protected override IObservable<bool>? Observe(StyledElement target)
    {
        return target is Button button
            ? button.GetObservable(Button.IsDefaultProperty)
            : null;
    }
}

#pragma warning restore IDE1006 // Names intentionally match AKCSS prefixes.
