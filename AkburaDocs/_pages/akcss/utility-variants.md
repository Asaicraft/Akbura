---
title: Utility Variants
summary: Define reactive AKCSS utility prefixes and control how active candidates participate in property conflict resolution.
---

## Utility variants

A utility variant is a markup extension that controls whether a prefixed AKCSS
utility is currently active.

```akbura
using Akbura.Markup;

<Border p-1
        ${sm}:p-2
        ${md}:p-3
        ${lg}:p-4 />
```

In this example, `${sm}`, `${md}`, and `${lg}` are utility variants.

A variant extension must return one of these types from `ProvideValue`:

- `bool`
- `IObservable<bool>`

A candidate participates in the AKCSS cascade only while the returned value is
`true`.

When an observable has not produced a value yet, or when its latest value is
`false`, the candidate is excluded. Another utility can then provide the
property value.

## Declaring a variant

Mark the markup extension class with `UtilityVariantAttribute`:

```csharp
using Akbura.Markup;

[UtilityVariant(
    10d,
    ConflictGroup = "WindowBreakpoints",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class WideExtension
{
    public IObservable<bool> ProvideValue(IServiceProvider services)
    {
        // Return whether the variant is currently active.
    }
}
```

The `Extension` suffix is omitted in markup:

```akbura
using Demo.Markup;

<Border p-2
        ${Wide}:p-6 />
```

Variant names follow the normal markup extension lookup rules. The namespace
containing the extension must be imported explicitly.

## Conflict resolution is property-based

Utilities do not conflict simply because they have similar names, use the same
variant, or belong to the same conflict group.

Akbura expands every utility into individual property-writing operations and
resolves each target property independently.

For example, suppose one utility writes:

```text
Width
Background
Padding
```

and another utility writes:

```text
Width
Height
```

Only the two `Width` operations compete.

The following operations remain active independently:

```text
Background
Padding
Height
```

This also means that assigning two variants to the same `ConflictGroup` does
not make unrelated utilities conflict.

## Resolution algorithm

For each target property, Akbura resolves active utility operations in the
following order.

### 1. Exclude inactive candidates

A prefixed candidate is excluded when its variant returns `false` or when its
observable has not produced a value yet.

### 2. Respect AKCSS operation priority

Variant ordering is considered only when candidates write the same target
property and have the same AKCSS operation priority.

For example, a regular style operation and an active style-trigger operation
are not made equal merely because their variants share a conflict group.

### 3. Resolve candidates inside each conflict group

Active prefixed candidates with the same non-empty `ConflictGroup` are ordered
by `Order`.

The candidate with the greater value wins:

```text
Order 20 > Order 10 > Order 1
```

When two candidates have the same `Order`, the candidate written later in
markup wins.

### 4. Compare different groups by source order

`Order` is not a global priority.

Winners from different conflict groups are compared by their position in
markup.

Candidates without a conflict group are also compared by source order.

For example, an `Order` of `1000` in one group does not automatically beat an
`Order` of `1` from another group.

### 5. Compare the prefixed winner with the unprefixed candidate

After Akbura selects the winning prefixed candidate, it compares that candidate
with the last unprefixed candidate writing the same property.

This comparison is controlled by `UnprefixedPrecedence`.

## UtilityVariantAttribute

```csharp
[AttributeUsage(
    AttributeTargets.Class,
    Inherited = false,
    AllowMultiple = false)]
public sealed class UtilityVariantAttribute : Attribute
```

The attribute can be applied once to a markup extension class.

### Order

```csharp
public double Order { get; }
```

`Order` controls priority between active prefixed candidates only when all of
the following conditions are true:

1. The candidates write the same target property.
2. The operations have the same AKCSS operation priority.
3. Both variants have the same non-empty `ConflictGroup`.

The greater value wins.

When values are equal, source order is used.

`Order` is ignored:

- between different conflict groups;
- when one or both variants have no group;
- when comparing a prefixed candidate with an unprefixed candidate.

Example:

```csharp
[UtilityVariant(
    10d,
    ConflictGroup = "Breakpoints")]
public sealed class MediumExtension
{
}
```

```csharp
[UtilityVariant(
    20d,
    ConflictGroup = "Breakpoints")]
public sealed class LargeExtension
{
}
```

While both variants are active, `LargeExtension` wins conflicting property
operations because `20` is greater than `10`.

### ConflictGroup

```csharp
public string? ConflictGroup { get; init; }
```

`ConflictGroup` identifies variants whose active candidates can be ordered by
`Order`.

```csharp
[UtilityVariant(
    10d,
    ConflictGroup = "Breakpoints")]
```

The group only affects operations that already conflict.

It does not create a conflict between unrelated properties.

A `null`, empty, or whitespace value means that the variant has no ordered
group. Such candidates are compared with other groups by source order.

Use the same group for variants that represent ordered alternatives, such as:

- minimum-width breakpoints;
- maximum-width breakpoints;
- mutually ordered interaction modes;
- ordered accessibility modes.

Use different groups when the variants represent independent conditions whose
relative priority should remain controlled by markup order.

### UnprefixedPrecedence

```csharp
public UnprefixedUtilityPrecedence UnprefixedPrecedence
{
    get;
    init;
}
```

This setting controls how the winning active prefixed candidate competes with
the last unprefixed candidate that writes the same property.

The default is:

```csharp
UnprefixedUtilityPrecedence.SourceOrder
```

The setting is applied only after the prefixed winner has been selected.

## UnprefixedUtilityPrecedence

### Below

```csharp
UnprefixedUtilityPrecedence.Below
```

The unprefixed candidate always wins while both candidates are active.

Markup order does not matter.

```akbura
<Border ${custom}:p-6
        p-2 />
```

```akbura
<Border p-2
        ${custom}:p-6 />
```

In both cases, `p-2` wins.

### SourceOrder

```csharp
UnprefixedUtilityPrecedence.SourceOrder
```

The candidate written later in markup wins.

```akbura
<Border ${custom}:p-6
        p-2 />
```

Here, `p-2` wins.

```akbura
<Border p-2
        ${custom}:p-6 />
```

Here, `${custom}:p-6` wins.

### Above

```csharp
UnprefixedUtilityPrecedence.Above
```

The active prefixed candidate always wins.

Markup order does not matter.

```akbura
<Border ${md}:p-6
        p-2 />
```

```akbura
<Border p-2
        ${md}:p-6 />
```

While `${md}` is active, `p-6` wins in both cases.

This behavior is used by the built-in breakpoint variants.

## Built-in breakpoint implementation

The built-in `${sm}`, `${md}`, `${lg}`, `${xl}`, and `${xxl}` variants observe
the current `TopLevel.ClientSize`.

All variants belong to the same conflict group.

Their increasing `Order` values ensure that the greatest active breakpoint
wins. They also use `UnprefixedUtilityPrecedence.Above`, so an unprefixed
utility cannot override an active breakpoint.

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Markup;

using static BreakpointsGroupKey;

file static class BreakpointsGroupKey
{
    public const string BreakpointsGroup = nameof(BreakpointsGroup);
}

public abstract class BreakpointMarkupExtension
{
    protected BreakpointPredicate IsActivatedPredicate
    {
        get;
        init;
    }


    public IObservable<bool>? ProvideValue(IServiceProvider? serviceProvider)
    {
        if (serviceProvider == null)
        {
            return null;
        }

        if (serviceProvider.GetService(typeof(IProvideValueTarget))
            is not IProvideValueTarget provideValueTarget)
        {
            return null;
        }

        if (provideValueTarget.TargetObject is not Visual target)
        {
            return null;
        }

        var topLevel = TopLevel.GetTopLevel(target);

        if (topLevel == null &&
            serviceProvider.GetService(typeof(IRootObjectProvider))
                is IRootObjectProvider rootObjectProvider)
        {
            topLevel = rootObjectProvider.IntermediateRootObject as TopLevel
                ?? rootObjectProvider.RootObject as TopLevel;
        }

        if (topLevel == null)
        {
            return null;
        }

        return new BreakpointObservable(
            topLevel.GetObservable(TopLevel.ClientSizeProperty),
            IsActivatedPredicate);
    }

    protected readonly unsafe struct BreakpointPredicate
    {
        private readonly delegate*<double, bool> _pointer;

        public BreakpointPredicate(delegate*<double, bool> pointer)
        {
            if (pointer == null)
            {
                throw new ArgumentNullException(nameof(pointer));
            }

            _pointer = pointer;
        }

        public bool Invoke(double width)
        {
            return _pointer(width);
        }
    }

    private sealed class BreakpointObservable : IObservable<bool>
    {
        private readonly IObservable<Size> _source;
        private readonly BreakpointPredicate _isActivated;

        public BreakpointObservable(
            IObservable<Size> source,
            BreakpointPredicate isActivated)
        {
            _source = source;
            _isActivated = isActivated;
        }

        public IDisposable Subscribe(IObserver<bool> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            return _source.Subscribe(
                new BreakpointObserver(observer, _isActivated));
        }
    }

    private sealed class BreakpointObserver : IObserver<Size>
    {
        private readonly IObserver<bool> _observer;
        private readonly BreakpointPredicate _isActivated;

        private bool _hasValue;
        private bool _lastValue;
        private bool _isStopped;

        public BreakpointObserver(
            IObserver<bool> observer,
            BreakpointPredicate isActivated)
        {
            _observer = observer;
            _isActivated = isActivated;
        }

        public void OnNext(Size size)
        {
            if (_isStopped)
            {
                return;
            }

            bool currentValue;

            try
            {
                currentValue = _isActivated.Invoke(size.Width);
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


#pragma warning disable IDE1006 // Naming Styles

[UtilityVariant(
    1,
    ConflictGroup = BreakpointsGroup,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class smExtension : BreakpointMarkupExtension
{
    public smExtension()
    {
        unsafe
        {
            IsActivatedPredicate =
                new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width)
    {
        return width >= 640d;
    }
}

[UtilityVariant(
    10,
    ConflictGroup = BreakpointsGroup,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class mdExtension : BreakpointMarkupExtension
{
    public mdExtension()
    {
        unsafe
        {
            IsActivatedPredicate =
                new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width)
    {
        return width >= 768d;
    }
}

[UtilityVariant(
    20,
    ConflictGroup = BreakpointsGroup,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class lgExtension : BreakpointMarkupExtension
{
    public lgExtension()
    {
        unsafe
        {
            IsActivatedPredicate =
                new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width)
    {
        return width >= 1024d;
    }
}

[UtilityVariant(
    30,
    ConflictGroup = BreakpointsGroup,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class xlExtension : BreakpointMarkupExtension
{
    public xlExtension()
    {
        unsafe
        {
            IsActivatedPredicate =
                new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width)
    {
        return width >= 1280d;
    }
}

[UtilityVariant(
    40,
    ConflictGroup = BreakpointsGroup,
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class xxlExtension : BreakpointMarkupExtension
{
    public xxlExtension()
    {
        unsafe
        {
            IsActivatedPredicate =
                new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width)
    {
        return width >= 1536d;
    }
}


#pragma warning restore IDE1006 // Naming Styles
```

## Breakpoint orders

| Variant | Active width | Order |
| --- | ---: | ---: |
| `${sm}` | `>= 640` | `1` |
| `${md}` | `>= 768` | `10` |
| `${lg}` | `>= 1024` | `20` |
| `${xl}` | `>= 1280` | `30` |
| `${xxl}` | `>= 1536` | `40` |

Consider this markup:

```akbura
<Border ${lg}:w-10
        w-5
        ${md}:w-7 />
```

At a width of `1100`:

- `${md}` is active;
- `${lg}` is active;
- `${lg}` has the greater `Order`;
- `${lg}:w-10` wins even though it appears before `w-5`;
- `w-5` cannot override it because breakpoint variants use
  `UnprefixedUtilityPrecedence.Above`.

At a width of `800`:

- `${md}` is active;
- `${lg}` is inactive;
- `${md}:w-7` wins.

Below `768`:

- both `${md}` and `${lg}` are inactive;
- `w-5` wins.

## Multiple properties

Variant priority is resolved independently for every property-writing
operation.

```akbura
<Border p-2
        bg-slate-800
        ${md}:p-6
        ${lg}:bg-blue-600 />
```

At the `md` breakpoint:

```text
Padding = p-6
Background = bg-slate-800
```

At the `lg` breakpoint:

```text
Padding = p-6
Background = bg-blue-600
```

`${lg}:bg-blue-600` does not disable `${md}:p-6`, because the candidates write
different properties.

## Equal order

When two active variants in the same group have equal `Order`, the candidate
written later wins.

```csharp
[UtilityVariant(
    10d,
    ConflictGroup = "Modes")]
public sealed class FirstExtension
{
}

[UtilityVariant(
    10d,
    ConflictGroup = "Modes")]
public sealed class SecondExtension
{
}
```

```akbura
<Border ${First}:p-2
        ${Second}:p-6 />
```

When both variants are active, `${Second}:p-6` wins.

Reversing the markup reverses the result:

```akbura
<Border ${Second}:p-6
        ${First}:p-2 />
```

Now `${First}:p-2` wins.

## Practical guidelines

Use a shared `ConflictGroup` when variants represent an ordered scale.

```text
sm < md < lg < xl < xxl
```

Use increasing `Order` values to represent that scale.

Use `UnprefixedUtilityPrecedence.Above` when an active variant must override
the default value regardless of markup order.

Use `SourceOrder` when the author should be able to override a variant by
placing another utility later.

Use `Below` when an unprefixed value must always remain authoritative.

Do not use `Order` as a global priority. It is intentionally local to one
non-empty conflict group and one conflicting property operation.

See also:

- [AKCSS Utilities](akcss/utilities)
- [Markup Extensions](akcss/markup-extensions)