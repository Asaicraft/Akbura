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

## Categories

- [Margin and padding](/akcss/spacing)

More built-in style categories will be documented here as they are added.