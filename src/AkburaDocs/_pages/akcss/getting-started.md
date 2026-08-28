---
title: AKCSS Getting Started
summary: Typed, reactive styles for Akbura components and Avalonia controls.
order: 1
---

AKCSS is Akbura's typed styling language. It complements Avalonia styles with C# expressions, reactive dependencies, typed property binding, and component-local style files.

## Your first reactive style

Create a companion style file next to the component. A component named `Counter.akbura` automatically discovers `Counter.akcss` in the same directory.

`Counter.akcss`:

```akcss
.style {
    @if(Background == Brushes.Red) {
        Padding: 15;
    }

    Padding: 10;
}
```

`Counter.akbura`:

```akbura
using Avalonia.Controls;
using Avalonia.Media;

state IBrush bg = Brushes.Aqua;

<Button class="style"
        Background={bg}
        Click={() => bg = bg == Brushes.Aqua ? Brushes.Red : Brushes.Aqua}>
    Current bg is {bg == Brushes.Aqua ? "Aqua" : "Red"}
</Button>
```

Clicking the button changes `bg`, which updates the button's `Background`. The `.style` rule observes the `Background` expression and runs its cascade again:

- with an aqua background, `Padding` is `10`;
- with a red background, the declaration inside `@if` sets `Padding` to `15`;
- changing back to aqua removes the conditional value and exposes the `Padding: 10` fallback again.

## Conditional priority

AKCSS maps declarations to Avalonia binding priorities:

| Declaration | Avalonia priority |
| --- | --- |
| A normal style declaration | `BindingPriority.Style` |
| A declaration inside an active `@if` | `BindingPriority.StyleTrigger` |

`StyleTrigger` has a higher priority than `Style`. This is why the conditional `Padding: 15` wins even though `Padding: 10` appears later in the file. When the condition becomes false, AKCSS clears the trigger value and the normal style value becomes visible again.

## How reactive dependencies are observed

AKCSS collects property references from every bound expression in a style, including conditions and property values. For each dependency it uses the best observation mechanism available:

1. **Avalonia property** - subscribes directly to the registered `AvaloniaProperty` observable.
2. **`INotifyPropertyChanged` object** - subscribes to `PropertyChanged` on the object that owns the referenced property.
3. **Non-observable property** - applies the value, but does not create an automatic subscription for that dependency.

When any observed dependency changes, AKCSS resets the values written by the style and evaluates the ordered cascade again.

### Expressions are reactive too

Observation is not limited to `@if`. Any property reference inside an expression can become a dependency:

```akcss
.sameSize {
    Width: Height;
}
```

Here `Height` is observed. Whenever the target's height changes, `.sameSize` is evaluated again and writes the new value to `Width`.

The same rule applies to larger expressions:

```akcss
.card {
    Width: Height * 2;

    @if(IsEnabled && Opacity > 0.5) {
        Padding: 20;
    }
}
```

Each resolvable property reference is tracked independently.

## Selectors

AKCSS supports untyped, typed, type-only, nested-type, and globally qualified selectors:

```akcss
.selector { }
Button.selector { }
(MyNamespace.Button) { }
(MyNamespace.Button.Nested).selector { }
(global::MyNs.Button) { }
(global::MyNs.Button).selector { }
```

| Form | Meaning |
| --- | --- |
| `.selector` | A named style that can contribute compatible properties to its target. |
| `Button.selector` | A named style restricted to `Button` and compatible derived controls. |
| `(MyNamespace.Button)` | A type-only selector. |
| `(MyNamespace.Button.Nested).selector` | A named style for a nested target type. |
| `(global::MyNs.Button)` | A type-only selector resolved from the global namespace. |
| `(global::MyNs.Button).selector` | A globally qualified typed class selector. |

Common Avalonia control names such as `Button` can be used directly. Parenthesized selectors are useful for fully qualified, generic, or nested C# type names.

## Using namespaces

A standalone `.akcss` file uses `@using` for C# namespaces:

```akcss
@using MyApplication.Controls;
@using MyApplication.Models;

(DashboardCard).highlighted {
    Opacity: 1;
}
```

AKCSS also provides these implicit namespaces:

```text
Avalonia
Avalonia.Layout
Avalonia.Media
Akbura
```

In addition, short Avalonia control names are resolved from `Avalonia.Controls`, so selectors such as `Button.primary` do not require a fully qualified name.

Use `global::` when a local namespace or type name would otherwise be ambiguous.

## Loading and importing styles

### Companion styles

AKCSS first searches the component's local styles. For `Counter.akbura`, the companion file is `Counter.akcss` in the same directory:

```text
Pages/
  Counter.akbura
  Counter.akcss
```

No import is required for the companion file. Inline `@akcss` blocks are local to the component as well.

### Importing an AKCSS module

Import shared styles in an `.akbura` component with a normal `using` whose logical name ends in `.akcss`:

`Counter.akbura`:

```akbura
using Akbura.Styles.akcss;

<Button class="myclass">Hello</Button>
```

The compiler searches local styles before imported modules. Imported modules are considered in `using` order.

One AKCSS file can import another with `@using`:

```akcss
@using Akbura.Styles.akcss;
```

An import name is the module's logical name, normally derived from the project's root namespace and file path.

## AKCSS and Avalonia styles together

AKCSS is not a replacement for Avalonia's standard styling system. Both can be used on the same control:

```akbura
<Border Classes="avalonia-card selected" class="akcssCard" />
```

- `Classes` contains standard Avalonia style classes.
- `class` contains AKCSS styles resolved by the Akbura compiler.

Use Avalonia styles for the existing Avalonia styling ecosystem and AKCSS where typed properties, C# expressions, reactive rules, or component-local styling are useful.