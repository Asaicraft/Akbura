using Akbura.Markup;

using Avalonia.Controls;
using Avalonia.Data;

using System;

namespace Akbura.FeatureGallery.Markup;

file static class UtilityBindingPriorityDemoGroup
{
    public const string Name = nameof(UtilityBindingPriorityDemoGroup);
}

#pragma warning disable IDE1006 // Markup extension names intentionally match markup.

[UtilityBindingPriority(Priority = BindingPriority.Animation)]
public sealed class importantExtension
{
    public ToggleSwitch? Source { get; set; }

    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding(nameof(ToggleSwitch.IsChecked))
        {
            Source = Source ?? throw new InvalidOperationException(
                "importantExtension requires a ToggleSwitch source."),
        };
    }
}

[UtilityVariant(
    10d,
    ConflictGroup = UtilityBindingPriorityDemoGroup.Name,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
[UtilityBindingPriority(PriorityMember = nameof(Priority))]
public sealed class priorityExtension
{
    public BindingPriority Priority { get; set; }

    public bool ProvideValue(IServiceProvider serviceProvider) => true;
}

#pragma warning restore IDE1006 // Markup extension names intentionally match markup.
