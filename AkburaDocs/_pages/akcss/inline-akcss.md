---
title: Inline AKCSS
summary: Define component-local AKCSS styles and utilities directly inside an Akbura component.
---

## Inline AKCSS

Inline AKCSS lets a component declare its own styles directly inside the `.akbura` file.

Use a top-level `@akcss` block:

```akbura
using Avalonia.Controls;

namespace Demo.Pages;

@akcss {
    .primary {
        Background: Blue;
        Foreground: White;
        Padding: (16, 8);
    }
}

<Button class="primary">
    Save
</Button>
````

The `primary` style belongs to this component and can be used without importing a separate `.akcss` file.

::: info
Inline AKCSS does not mean assigning styles directly through markup attributes. It defines regular AKCSS classes locally inside the component.
:::

## Reactive inline styles

Inline styles have the same reactive behavior as styles declared in external `.akcss` files:

```akbura
using Avalonia.Controls;

namespace Demo.Pages;

@akcss {
    Button.primary {
        Background: Blue;
        Padding: 10;

        @if(IsPointerOver) {
            Background: DarkBlue;
            Padding: 15;
        }
    }
}

<Button class="primary">
    Hover over me
</Button>
```

The style observes `IsPointerOver`. When its value changes, AKCSS evaluates the style again.

Properties inside `@if` use `BindingPriority.StyleTrigger`, so they take priority over regular declarations using `BindingPriority.Style`.

When the condition becomes false, the trigger values are removed and the regular values are restored.

## AKCSS using directives

C# namespaces used by an inline AKCSS block are imported with `@using`:

```akbura
@akcss {
    @using Avalonia.Media;

    Button.primary {
        Background: Brushes.DodgerBlue;
        Foreground: Brushes.White;
    }
}
```

Notice the difference:

```akbura
using Avalonia.Controls;

@akcss {
    @using Avalonia.Media;
}
```

* `using` imports a namespace into the Akbura component.
* `@using` imports a namespace into the AKCSS module.

## Importing external styles

An inline block can import styles from another `.akcss` module:

```akbura
@akcss {
    @using Demo.Styles.Shared.akcss;

    Button.primary {
        @apply surface;
        Padding: 12;
    }
}
```

Here, `surface` is resolved from `Demo.Styles.Shared.akcss`, while `primary` remains local to the component.

## Inline utilities

Component-local utilities can be declared inside `@utilities`:

```akbura
@akcss {
    @utilities {
        .fade {
            Opacity: 0.75;
        }

        .square-(double size) {
            Width: size;
            Height: size;
        }
    }
}

<Button fade square-48>
    Square button
</Button>
```

Inline utilities are available immediately in the markup of the same component.

When a local utility and an imported utility have the same name, AKCSS searches the local inline module first.

## Selectors

Inline AKCSS supports the same selectors as external styles:

```akbura
@akcss {
    .card {
        Padding: 10;
    }

    Button.primary {
        Background: Blue;
    }

    (Demo.Controls.SpecialButton) {
        Padding: 12;
    }

    (global::Demo.Controls.SpecialButton).accent {
        Background: Orange;
    }
}
```

## Combining inline and external AKCSS

Inline AKCSS is best suited for styles used by only one component. External `.akcss` files are better for shared styles and utilities.

Both approaches can be used together:

```akbura
using Avalonia.Controls;
using Demo.Styles.Shared.akcss;

namespace Demo.Pages;

@akcss {
    .localCard {
        Padding: 16;
    }
}

<StackPanel>
    <Border class="localCard">
        Local component style
    </Border>

    <Button class="sharedButton">
        Shared external style
    </Button>
</StackPanel>
```

Use inline AKCSS for component-specific styling and external `.akcss` modules for reusable application-wide styles.
