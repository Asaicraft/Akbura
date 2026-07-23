---
title: Grid
summary: Define Grid rows and columns with Akbura's compact Grid definition syntax.
---


Avalonia's `Grid` arranges child controls into rows and columns.

In `.akbura` markup, the `RowDefinitions` and `ColumnDefinitions` properties support a compact literal syntax:

```akbura
<Grid
    ColumnDefinitions="*, 100, Auto"
    RowDefinitions="Auto 2* 48">
    <!-- content -->
</Grid>
```

This syntax creates the corresponding Avalonia row and column definitions without requiring each definition to be declared separately.

::: info
Grid definition literals are available in `.akbura` markup attributes.
:::

They are not currently supported as a special AKCSS assignment value.

## Grid lengths

Each definition contains a Grid length.

The following forms are supported:

| Syntax | Meaning                               |
| ------ | ------------------------------------- |
| `Auto` | Size the row or column to its content |
| `100`  | Use `100` device-independent pixels   |
| `*`    | Use one proportional star unit        |
| `2*`   | Use two proportional star units       |

For example:

```akbura
<Grid ColumnDefinitions="Auto, *, 2*, 100" />
```

This creates four columns:

1. An automatically sized column.
2. A column using one share of the available space.
3. A column using two shares of the available space.
4. A fixed-width column of `100` device-independent pixels.

## Separating definitions

Definitions may be separated either by commas or by whitespace.

Using commas:

```akbura
<Grid ColumnDefinitions="*, 100, Auto" />
```

Using whitespace:

```akbura
<Grid RowDefinitions="Auto * 2* 48" />
```

Use one separator style within the same value.

When the value contains top-level commas, those commas delimit the definitions. Otherwise, the definitions are separated by whitespace.

## Minimum and maximum sizes

Grid definitions may include minimum and maximum constraints.

```akbura
<Grid
    ColumnDefinitions="
        min(100),
        max(300),
        min-max(100, 300),
        min-max(100, *, 300),
        min-max(0, Auto, 100)" />
```

### Minimum size

Use `min(...)` to create a star-sized definition with a minimum size:

```akbura
<Grid ColumnDefinitions="min(100)" />
```

This is equivalent to a `*` column whose minimum width is `100`.

### Maximum size

Use `max(...)` to create a star-sized definition with a maximum size:

```akbura
<Grid ColumnDefinitions="max(300)" />
```

This is equivalent to a `*` column whose maximum width is `300`.

### Minimum and maximum size

Use `min-max(...)` to specify both constraints:

```akbura
<Grid ColumnDefinitions="min-max(100, 300)" />
```

The two-argument form uses `*` as the Grid length.

You may also provide the Grid length explicitly:

```akbura
<Grid
    ColumnDefinitions="
        min-max(100, 2*, 300),
        min-max(0, Auto, 100)" />
```

Supported forms include:

| Syntax                  | Result                          |
| ----------------------- | ------------------------------- |
| `min(100)`              | `*` with a minimum of `100`     |
| `max(300)`              | `*` with a maximum of `300`     |
| `min-max(100, 300)`     | `*` constrained to `100...300`  |
| `min-max(100, *, 300)`  | `*` constrained to `100...300`  |
| `min-max(100, 2*, 300)` | `2*` constrained to `100...300` |
| `min-max(0, Auto, 100)` | `Auto` constrained to `0...100` |

## Validation rules

Grid definition values are validated by the Akbura compiler.

The following rules apply:

* Pixel values must be non-negative.
* Minimum values must be non-negative.
* Maximum values must be non-negative.
* In `min-max(...)`, the minimum value cannot be greater than the maximum value.

Invalid definitions produce a compiler diagnostic instead of being deferred until runtime.

## Complete example

```akbura
<Grid
    ColumnDefinitions="Auto, min-max(120, *, 320), 2*"
    RowDefinitions="Auto * 48">

    <!-- Grid content -->
</Grid>
```

This Grid contains:

* Three columns:

  * an automatically sized column;
  * a star-sized column constrained to `120...320`;
  * a column receiving two proportional shares of the remaining space;
* Three rows:

  * an automatically sized row;
  * a row filling the remaining space;
  * a fixed row with a height of `48`.


Для файла хорошо подойдёт путь:

```text
AkburaDocs/_pages/akbura/grid.md
````