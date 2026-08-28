using Akbura.Markup;

using Avalonia;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using System;

namespace Akbura.FeatureGallery.Markup;

using static InteractionUtilityVariantGroupKey;

file static class InteractionUtilityVariantGroupKey
{
    public const string Interaction =
        nameof(Interaction);
}

public abstract class InputStateUtilityVariantExtension
{
    public IObservable<bool>? ProvideValue(
        IServiceProvider? serviceProvider)
    {
        if (serviceProvider?.GetService(
                typeof(IProvideValueTarget))
            is not IProvideValueTarget provideValueTarget)
        {
            return null;
        }

        if (provideValueTarget.TargetObject
            is not InputElement target)
        {
            return null;
        }

        return Observe(target);
    }

    protected abstract IObservable<bool> Observe(
        InputElement target);
}

#pragma warning disable IDE1006 // Naming Styles

[UtilityVariant(
    10d,
    ConflictGroup = Interaction,
    UnprefixedPrecedence =
        UnprefixedUtilityPrecedence.Above)]
public sealed class demoHoverExtension
    : InputStateUtilityVariantExtension
{
    protected override IObservable<bool> Observe(
        InputElement target)
    {
        return target.GetObservable(
            InputElement.IsPointerOverProperty);
    }
}

[UtilityVariant(
    20d,
    ConflictGroup = Interaction,
    UnprefixedPrecedence =
        UnprefixedUtilityPrecedence.Above)]
public sealed class demoFocusExtension
    : InputStateUtilityVariantExtension
{
    protected override IObservable<bool> Observe(
        InputElement target)
    {
        return target.GetObservable(
            InputElement.IsKeyboardFocusWithinProperty);
    }
}

#pragma warning restore IDE1006 // Naming Styles