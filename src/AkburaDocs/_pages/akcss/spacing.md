---
title: Margin and Padding
summary: Compose per-side margin and padding utilities without losing unaffected Thickness values.
---

Import Akbura's built-in styles before using spacing utilities:

```akbura
using Akbura.Styles.akcss;
```

## Spacing scale

Margin and padding values use the dynamic `--spacing` resource. Its default value is `4`:

| Utility value | Avalonia value |
| --- | ---: |
| `1` | `4` |
| `2` | `8` |
| `3` | `12` |
| `4` | `16` |

For example:

```akbura
<Border m-4 p-3 />
```

This produces a margin of `16` and padding of `12` on every side.

## Margin

Margin utilities are available on every Avalonia `Control`:

| Utility | Sides |
| --- | --- |
| `m-{value}` | all sides |
| `mx-{value}` | left and right |
| `my-{value}` | top and bottom |
| `mt-{value}` | top |
| `mr-{value}` | right |
| `mb-{value}` | bottom |
| `ml-{value}` | left |

Utilities can be composed:

```akbura
<Border m-4 mx-2 mt-1 />
```

With the default spacing scale, the resulting margin is:

```text
Left   = 8
Top    = 4
Right  = 8
Bottom = 16
```

## Padding

Padding utilities are available for `TemplatedControl`, `Decorator`, and `AkburaControl`, including controls such as `Button` and `Border`:

| Utility | Sides |
| --- | --- |
| `p-{value}` | all sides |
| `px-{value}` | left and right |
| `py-{value}` | top and bottom |
| `pt-{value}` | top |
| `pr-{value}` | right |
| `pb-{value}` | bottom |
| `pl-{value}` | left |

```akbura
<Button px-4 py-2>
    Save
</Button>
```

## Per-side utilities compose correctly

Avalonia represents padding as one `Thickness` value. If `p-3` and `pt-2` were implemented as ordinary `Padding` setters, the second setter would need to replace the entire value:

```text
p-3  -> Padding = 12,12,12,12
pt-2 -> Padding = 0,8,0,0
```

The final result would incorrectly lose the left, right, and bottom padding.

Akbura's built-in styles avoid this problem:

```akbura
<Border p-3 pt-2 />
```

The result is:

```text
Left   = 12
Top    = 8
Right  = 12
Bottom = 12
```

In spacing units, `pt-2` overrides only the top side of `p-3`; the other three sides remain at `3`.

## How it works

Built-in spacing utilities write independent attached properties instead of replacing `Padding` or `Margin` directly:

```akcss
TemplatedControl.p-(double value) {
    AkburaControl.ExplicitLeftPadding:
        value * Amx.DynamicResource<double>("--spacing");
    AkburaControl.ExplicitTopPadding:
        value * Amx.DynamicResource<double>("--spacing");
    AkburaControl.ExplicitRightPadding:
        value * Amx.DynamicResource<double>("--spacing");
    AkburaControl.ExplicitBottomPadding:
        value * Amx.DynamicResource<double>("--spacing");
}

TemplatedControl.pt-(double value) {
    AkburaControl.ExplicitTopPadding:
        value * Amx.DynamicResource<double>("--spacing");
}
```

At runtime, `AkburaControl` combines the four nullable side values into the control's real `Thickness`. A side without an explicit value falls back to the corresponding side from the original `Padding` or `Margin` value.

This also preserves values assigned through regular Avalonia properties:

```akbura
<Border Padding="1,2,3,4" pt-2 />
```

With the default spacing scale, only the top becomes `8`; the left, right, and bottom retain their original values.

The implementation is available in [`AkburaControl`](https://github.com/Asaicraft/Akbura/blob/7ef47054b6221fa1dd4a0af9751150be1c668e4a/Akbura/AkburaControl.cs#L482).