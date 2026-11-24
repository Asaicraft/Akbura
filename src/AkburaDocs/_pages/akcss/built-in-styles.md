---
title: Built-in Styles
summary: Tailwind-inspired, type-safe utilities included with Akbura.
---

Akbura includes a collection of typed AKCSS utilities for common Avalonia layout and appearance tasks.

Import them in an `.akbura` component:

```akbura
using Akbura.Styles.akcss;
```

Utilities are then available as concise markup attributes:

```akbura
<Button w-40 px-4 py-2 bg-blue-500 text-white rounded-md>
    Continue
</Button>
```

Built-in styles are type-aware. A utility is available only when its target type is compatible with the control on which it is used. For example, padding utilities target controls that expose a `Padding` property, while margin utilities work with every Avalonia `Control`.

Most built-in values come from dynamic Avalonia resources such as `--spacing`, `--color-blue-500`, `--radius-md`, and `--shadow-lg`. Applications can replace these resources to customize the theme without redefining each utility.

## Align a control by axis

Use `self-x-*` to set horizontal alignment and `self-y-*` to set vertical alignment. Both accept `start`, `center`, `end`, `stretch`, and `auto`. As with `self-auto`, the `auto` suffix uses Avalonia's `Stretch` value.

```akbura
<Button self-x-center self-y-end Content="Continue" />
```

## Button presenter utilities

Use `presenter-*` markup utilities to style the `ContentPresenter` named `PART_ContentPresenter` in a button template. The Fluent theme uses this part. Custom templates can opt in by using the same part name.

```akbura
using Avalonia.Controls;
using Akbura.Markup;
using Akbura.Styles.akcss;

<Button Content="Continue"
        presenter-bg-blue-500
        ${hover}:presenter-bg-blue-700
        presenter-border-2 presenter-border-blue-600
        presenter-rounded-md presenter-padding-8 presenter-text-white />
```

Colors, named radii, and shadows use the shared theme resources. `presenter-padding-8` sets eight device-independent pixels on each side.

## Categories

- [Margin and padding](akcss/spacing)

More built-in style categories will be documented here as they are added.
