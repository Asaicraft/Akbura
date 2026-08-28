---
title: Markup Extensions
summary: Supply utility arguments and variants through typed, reactive markup extensions.
---

Akbura can evaluate markup extension classes in regular attributes and in
AKCSS utility attributes. A markup extension is a class with a public instance
`ProvideValue()` or `ProvideValue(IServiceProvider)` method.

The `Extension` suffix is optional:

```akbura
using Demo.Markup;

<Border p-${GalleryPadding 4} />
```

Both `GalleryPadding` and `GalleryPaddingExtension` resolve this class:

```csharp
namespace Demo.Markup;

public sealed class GalleryPaddingExtension
{
    public GalleryPaddingExtension(double value)
    {
        Value = value;
    }

    public double Value { get; }

    public double ProvideValue(IServiceProvider services)
    {
        return Value;
    }
}
```

The extension namespace must be imported explicitly. `Akbura.Markup` is not
added implicitly either:

```akbura
using Akbura.Markup;

<Border p-1 ${md}:p-3 />
```

## Utility arguments

A markup extension can supply an argument to a parameterized utility:

```akcss
@using Avalonia;
@using Avalonia.Controls;

@utilities {
    Decorator.p-(double value) {
        Padding: new Thickness(value);
    }
}
```

```akbura
using Demo.Markup;

state double spacing = 12;

<Border p-${GalleryPadding {spacing}} />
```

For a utility parameter of type `T`, `ProvideValue` may return:

| Result | Behavior |
| --- | --- |
| `T` | Applies the value immediately. |
| `IObservable<T>` | Reapplies the utility whenever the observable publishes a value. |
| `IObservable<object>` | Converts each published value to `T` at runtime. |
| `BindingBase` | Binds a hidden attached property on the actual utility target and reapplies the utility when the binding changes. |

An observable that has not published a value does not participate in the
utility cascade. This lets an earlier utility with the same conflict key act as
a fallback. Completion keeps the last value. An observable error is reported
immediately.

The `IObservable<object>` form is useful when an extension cannot expose a
generic result type. Every published value still has to be compatible with the
utility parameter type.

## Dynamic arguments

If an extension contains a component expression, Akbura recreates it during
each component update:

```akbura
<Border p-${GalleryPadding {spacing + 1}} />
```

Expressions inside the extension are bound as normal C# expressions. They are
included in `GetCSharpSymbolReferences` and preserve source locations through
generated `#line` directives.

Literal or otherwise component-independent extensions are created when their
runtime source is attached. They are not recreated by every component update.

## Utility variants

A markup extension before a colon controls whether a utility candidate is
active:

```akbura
using Akbura.Markup;

<Border p-1
        ${sm}:p-2
        ${md}:p-3 />
```

A prefix is not a special kind of markup extension. Existing markup extensions
can be used directly when their resolved value supplies a boolean condition:

```akbura
<ToggleSwitch x.Name="MyToggle" />

<Border ${DynamicResource MyKey}:p-5
        ${StaticResource MyBoolValue}:p-7
        ${Binding #MyToggle.IsChecked}:p-10 />
```

This means resource lookup and Avalonia bindings can control utilities without
a custom extension class. A direct extension may return `bool` or
`IObservable<bool>`. Extensions such as `DynamicResource` and `Binding` can
also provide the condition through an Avalonia binding. The observable and
binding forms reevaluate only the affected element and conflicting property.

`UtilityVariantAttribute` is optional. It does not make an extension usable as
a prefix. It only supplies `Order`, `ConflictGroup`, and
`UnprefixedPrecedence` for resolving active candidates that write the same
property. Without the attribute, prefixed candidates use normal source order.

The older form:

```akbura
<Border md:p-3 />
```

is parsed only for error recovery and produces a diagnostic. Use:

```akbura
<Border ${md}:p-3 />
```

Custom ordering and unprefixed fallback behavior can be configured with
`UtilityVariantAttribute`.

See [Utility Variants](akcss/utility-variants) for the complete property-level
conflict-resolution algorithm, `ConflictGroup`, `Order`, built-in breakpoint
implementation, and `UnprefixedUtilityPrecedence` modes.

## Utility binding priority

A prefix extension can select the Avalonia binding layer used after AKCSS has
selected a winning property operation:

```csharp
using Akbura.Markup;
using Avalonia.Data;

[UtilityBindingPriority(
    Priority = BindingPriority.Animation)]
public sealed class importantExtension
{
    public BindingBase ProvideValue(IServiceProvider services)
    {
        return new Binding("IsChecked")
        {
            ElementName = "ImportantToggle"
        };
    }
}
```

```akbura
<ToggleSwitch x.Name="ImportantToggle" />

<Border Margin="10"
        ${important}:m-12 />
```

The binding controls whether `m-12` participates. While it is active, the
winning `Margin` operation is installed at `BindingPriority.Animation`.
When it becomes inactive, Akbura disposes only that contribution and Avalonia
reveals the previous `Margin="10"` value.

Use exactly one priority source:

```csharp
[UtilityBindingPriority(
    Priority = BindingPriority.Template)]
public sealed class fixedPriorityExtension
{
}
```

```csharp
[UtilityBindingPriority(
    PriorityMember = nameof(Priority))]
public sealed class priorityExtension
{
    public BindingPriority Priority { get; set; }
}
```

`PriorityMember` may name an accessible instance field or readable instance
property whose type is exactly `BindingPriority`. Akbura creates one extension
instance for each prefix invocation, applies its constructor and named
properties, calls `ProvideValue`, and reads the priority from that same
instance. A refresh first disposes the old property contributions and then
creates the next instance.

The supported priorities are:

- `BindingPriority.Animation`
- `BindingPriority.StyleTrigger`
- `BindingPriority.Template`
- `BindingPriority.Style`

`LocalValue`, `Inherited`, `Unset`, and unknown enum values are rejected.
Priority-aware utilities may write only `StyledProperty` and
`AttachedProperty` values. Dynamic resources are bound at the requested
priority; ordinary values are installed as disposable Avalonia contributions.

This attribute does not participate in conflict resolution. It can be combined
with `UtilityVariantAttribute`, but `Order`, `ConflictGroup`, and
`UnprefixedPrecedence` still select the AKCSS winner independently.

## Lifecycle

Akbura owns every subscription and binding created for utility markup
extensions.

1. Sources are attached to the actual target control.
2. A new observable or binding value reevaluates only its utility conflict.
3. A component update performs the complete AKCSS cascade and recreates
   extensions that contain C# expressions.
4. Detaching the target disposes subscriptions and bindings.
5. Attaching it again evaluates the extensions and subscribes again.

`BindingBase` may temporarily produce no value while `DataContext` is being
inherited. During that period the utility candidate is excluded and its
fallback can apply.

## AKCSS resource helpers

AKCSS also provides two compiler-supported resource methods:

```csharp
Amx.StaticResource<T>(object? key);
Amx.DynamicResource<T>(object? key);
```

They provide typed access to resources from AKCSS expressions.

| Method | Behavior |
| --- | --- |
| `Amx.StaticResource<T>(key)` | Resolves the resource as `T` without creating a dynamic resource binding. |
| `Amx.DynamicResource<T>(key)` | Observes the resource and updates the target property when its value changes. |

These methods are compiler interception points. They throw if called directly
from ordinary C# code.

### Static resources

Use `StaticResource<T>` when the resource does not need to update after it has
been resolved:

```akcss
@using Avalonia.Media;

Button.brand {
    Background: Amx.StaticResource<IBrush>("BrandBrush");
}
```

### Dynamic resources

Use `DynamicResource<T>` when changing the resource should update the styled
property:

```akcss
Control.w-(double width) {
    Width: width * Amx.DynamicResource<double>("--spacing");
}
```

With the default `--spacing` value of `4`, `w-10` produces a width of `40`.
The generated utility obtains the resource observable, converts every new
value using the original expression, binds the result, and tracks the binding
for disposal.

`Amx.Extend<T>` remains an interception placeholder. Utility markup extensions
use `${...}` and do not call it.

## Diagnostics

Akbura reports diagnostics before code generation when:

- the extension type is missing or ambiguous;
- a public constructor cannot accept the positional arguments;
- a named extension property is missing or inaccessible;
- `ProvideValue` is missing;
- a utility argument cannot produce its declared parameter type;
- a utility variant does not produce `bool` or `IObservable<bool>`;
- a nested C# expression or its conversion is invalid.
