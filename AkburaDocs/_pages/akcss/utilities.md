---
title: AKCSS Utilities
summary: Create reusable, typed styling utilities and apply them directly from Akbura markup.
---

## AKCSS Utilities

Utilities are small reusable styles that can be applied directly as markup attributes.

```akbura
using Avalonia.Controls;
using Akbura.Styles.akcss;

<Button w-40 h-10 px-4 bg-blue-500 text-white rounded-md>
    Continue
</Button>
```

Unlike regular AKCSS classes, utilities do not use the `class` attribute:

```akbura
<Button class="primary" w-40 />
```

Here:

- `primary` is an AKCSS class.
- `w-40` is an AKCSS utility.

## Declaring utilities

Custom utilities are declared inside an `@utilities` section:

```akcss
@using Avalonia.Controls;

@utilities {
    Control.inactive {
        Opacity: 0.5;
        IsHitTestVisible: false;
    }
}
```

The utility is then used as a flag-style attribute:

```akbura
<Button inactive>
    Disabled action
</Button>
```

A utility without parameters uses its complete selector name. Names may contain multiple segments:

```akcss
@utilities {
    Control.self-start {
        HorizontalAlignment: Left;
        VerticalAlignment: Top;
    }
}
```

```akbura
<Button self-start>
    Aligned to start
</Button>
```

## Parameterized utilities

Parameters are declared after the utility name:

```akcss
@using Avalonia.Controls;

@utilities {
    Control.square-(double size) {
        Width: size;
        Height: size;
    }
}
```

Pass the argument as another segment of the markup attribute:

```akbura
<Button square-48>
    Square button
</Button>
```

In this example, `48` is converted to the `double size` parameter.

A utility can have multiple parameters:

```akcss
@using Akbura;
@using Avalonia.Controls;
@using Avalonia.Media;

@utilities {
    Border.frame-(string color)-(int shade) {
        BorderBrush:
            Amx.DynamicResource<IBrush>(
                "--color-" + color + "-" + shade);
    }
}
```

```akbura
<Border frame-blue-500>
    Content
</Border>
```

The arguments are resolved as:

```text
color = "blue"
shade = 500
```

Utility parameters are typed. AKCSS supports regular C# types, including:

- `double`
- `int`
- `string`
- enums
- custom types

An incompatible argument produces a compiler diagnostic.

## Expression arguments

Use `{...}` when a utility argument comes from a C# expression:

```akbura
using Avalonia.Controls;

@akcss {
    @utilities {
        Control.square-(double size) {
            Width: size;
            Height: size;
        }
    }
}

state double buttonSize = 48;

<Button square-{buttonSize}>
    Dynamic size
</Button>
```

Expressions are bound in the current component scope and can reference states, parameters, local variables, and other C# expressions.

```akbura
state double width = 20;
state double scale = 2;

<Button square-{width * scale} />
```

## Conditional utilities

A utility can be applied conditionally with an expression prefix:

```akbura
using Avalonia.Controls;
using Akbura.Styles.akcss;

state bool isBusy = false;

<Button {isBusy}:opacity-50>
    Save
</Button>
```

The `opacity-50` utility is applied only while `isBusy` is `true`.

Another common example is conditional visibility:

```akbura
<TextBlock {isBusy}:hidden>
    Content is ready
</TextBlock>
```

The condition must be a valid boolean expression:

```akbura
<Button {count > 10}:hidden />
<Button {user.IsAdmin}:visible />
```

## Conditional declarations inside utilities

Utilities may also contain reactive `@if` declarations:

```akcss
@using Avalonia.Controls;

@utilities {
    Control.interactive {
        Opacity: 0.8;

        @if(IsPointerOver) {
            Opacity: 1;
        }
    }
}
```

```akbura
<Button interactive>
    Hover over me
</Button>
```

The utility observes `IsPointerOver` and reevaluates when the property changes.

Declarations inside `@if` use `BindingPriority.StyleTrigger`, giving them priority over regular utility declarations using `BindingPriority.Style`.

## Enum parameters

Enum members can be passed as utility segments:

```akcss
@using Avalonia;
@using Avalonia.Controls;

@utilities {
    Control.align-(HorizontalAlignment alignment) {
        HorizontalAlignment: alignment;
    }
}
```

```akbura
<Button align-Center />
<Button align-Right />
```

Enum member names are resolved using the declared parameter type.

An expression can also be used:

```akbura
state HorizontalAlignment alignment =
    HorizontalAlignment.Center;

<Button align-{alignment} />
```

## Type-specific utilities

A utility can restrict itself to a particular control type:

```akcss
@using Avalonia.Controls;

@utilities {
    StackPanel.gap-(double value) {
        Spacing: value;
    }

    Grid.gap-(double value) {
        ColumnSpacing: value;
        RowSpacing: value;
    }
}
```

The compiler selects the utility compatible with the target control:

```akbura
<StackPanel gap-12 />

<Grid gap-12 />
```

A type-specific utility can also be used on derived controls.

Applying it to an incompatible type produces a utility-not-found diagnostic:

```akbura
<!-- StackPanel.gap cannot be applied to TextBlock -->
<TextBlock gap-12 />
```

Parenthesized and fully qualified types are supported:

```akcss
@utilities {
    (Demo.Controls.Card).compact {
        Padding: 4;
    }

    (global::Demo.Controls.SpecialButton).wide {
        Width: 200;
    }
}
```

## Using utilities with `@apply`

Utilities can be composed into a regular AKCSS class with `@apply`:

```akcss
@using Akbura.Styles.akcss;

.card {
    @apply w-full p-4 bg-slate-100 rounded-md shadow-sm;
}
```

```akbura
<Border class="card">
    Card content
</Border>
```

`@apply` can combine both styles and utilities:

```akcss
@using Demo.Styles.Shared.akcss;
@using Akbura.Styles.akcss;

.panel {
    @apply surface w-full p-4 rounded-lg;
}
```

## Where utilities are resolved from

When a utility is used in markup, Akbura searches in this order:

1. Utilities declared in an inline `@akcss` block.
2. Utilities from the component's companion `.akcss` file.
3. Imported `.akcss` modules, in `using` order.

For example, `Counter.akbura` automatically sees utilities from `Counter.akcss`:

```text
Counter.akbura
Counter.akcss
```

Shared utilities are imported explicitly:

```akbura
using Demo.Styles.Utilities.akcss;
```

```akbura
<Button custom-utility />
```

If the same selector is declared more than once in the same resolution layer, the compiler reports an ambiguous utility diagnostic.

## Built-in utilities

Akbura provides Tailwind-inspired built-in utilities through:

```akbura
using Akbura.Styles.akcss;
```

Common categories include:

| Category | Examples |
|---|---|
| Size | `w-10`, `h-8`, `size-12`, `w-full`, `h-auto` |
| Min/max size | `min-w-10`, `max-h-40` |
| Margin | `m-4`, `mx-2`, `mt-6`, `mb-4` |
| Padding | `p-4`, `px-3`, `py-2`, `pl-4` |
| Layout | `hidden`, `visible`, `self-center` |
| Grid | `col-1`, `row-2`, `col-span-2` |
| Spacing | `gap-4`, `gap-x-2`, `gap-y-3` |
| Opacity | `opacity-50`, `opacity-100` |
| Colors | `bg-blue-500`, `text-white` |
| Typography | `text-lg`, `font-bold`, `text-center` |
| Borders | `border-1`, `border-slate-300` |
| Radius | `rounded-md`, `rounded-full` |
| Shadows | `shadow`, `shadow-lg`, `shadow-2xl` |

Utilities are type-aware. For example:

- `gap-4` works differently for `StackPanel` and `Grid`.
- `rounded-md` is available for controls supporting `CornerRadius`.
- `text-center` is available for text controls.
- `bg-blue-500` is resolved for controls supporting `Background`.

## Spacing scale

Built-in numeric sizing and spacing utilities use the shared `--spacing` resource.

The default value is `4`:

```text
w-10 → Width = 10 × 4 = 40
p-3  → Padding = 3 × 4 = 12
gap-4 → Spacing = 4 × 4 = 16
```

Built-in colors, radii, font sizes, weights, and shadows also use dynamic Avalonia resources:

```text
--color-blue-500
--radius-md
--text-lg
--font-weight-bold
--shadow-lg
```

Because these are dynamic resources, applications can customize the theme without redefining every utility.