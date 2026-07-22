---
title: AKCSS Value Syntax
summary: Write typed C# expressions, Thickness tuples, colors, brushes, corner radii, and Grid definitions.
---

AKCSS property values are strongly typed. The compiler resolves the property first, then interprets the expression using that property's type.

Most values are regular C# expressions:

```akcss
@using Avalonia;
@using Avalonia.Media;

Button.card {
    Opacity: 0.85;
    IsVisible: true;
    HorizontalAlignment: Center;
    FontWeight: FontWeight.Bold;
    Background: Brushes.SlateBlue;
    CornerRadius: new CornerRadius(8);
}
```

AKCSS also provides concise syntax for several common Avalonia value types.

## Expected-type values

Static members of the target property type can be written without repeating the type name:

```akcss
Button.card {
    HorizontalAlignment: Center;
    VerticalAlignment: Bottom;
}
```

The compiler uses the expected property type, so these values are resolved as:

```csharp
HorizontalAlignment.Center
VerticalAlignment.Bottom
```

Qualified members and explicit casts are also supported:

```akcss
Button.card {
    HorizontalAlignment: HorizontalAlignment.Right;
    VerticalAlignment: (VerticalAlignment)2;
    FontWeight: (FontWeight)700;
}
```

This behavior also applies to custom enum properties.

## Thickness

Properties such as `Padding`, `Margin`, and `BorderThickness` use `Avalonia.Thickness`.

### Uniform value

A single number is applied to every side:

```akcss
Button.card {
    Padding: 10;
}
```

Equivalent C# value:

```csharp
new Thickness(10, 10, 10, 10)
```

### Horizontal and vertical values

A two-item tuple uses the first value for left and right and the second value for top and bottom:

```akcss
Button.card {
    Padding: (16, 8);
}
```

```text
Left   = 16
Top    = 8
Right  = 16
Bottom = 8
```

### Individual sides

A four-item tuple follows Avalonia's `left, top, right, bottom` order:

```akcss
Button.card {
    Margin: (4, 8, 12, 16);
}
```

### Named sides

Named components make partial values easier to read:

```akcss
Button.card {
    Padding: (top: 8, bottom: 12);
    Margin: (horizontal: 16, vertical: 8);
}
```

Supported names are:

| Name | Applied sides |
| --- | --- |
| `left` | left |
| `top` | top |
| `right` | right |
| `bottom` | bottom |
| `horizontal` | left and right |
| `vertical` | top and bottom |

Sides omitted from a named tuple receive `0`.

Tuple items may contain runtime expressions:

```akcss
@utilities {
    Control.space-(double size) {
        Margin: (
            horizontal: size * Amx.DynamicResource<double>("--spacing"),
            vertical: size
        );
    }
}
```

Regular C# expressions returning `Thickness` remain available:

```akcss
Button.card {
    Margin: new Thickness(10, 5) - new Thickness(3, 2);
}
```

## Colors and brushes

Color literals are available when the target property is `Avalonia.Media.Color` or `IBrush`.

### Named colors

Avalonia color names can be written as identifiers or strings:

```akcss
Button.primary {
    Background: DodgerBlue;
    Foreground: "White";
}
```

Named values are resolved from `Avalonia.Media.Colors`.

### Hex colors

Hex values must be string literals:

```akcss
Button.primary {
    Background: "#369";
    BorderBrush: "#F369";
    Foreground: "#336699";
    Background: "#80336699";
}
```

Supported formats are:

| Format | Meaning |
| --- | --- |
| `#RGB` | red, green, blue |
| `#ARGB` | alpha, red, green, blue |
| `#RRGGBB` | red, green, blue |
| `#AARRGGBB` | alpha, red, green, blue |

### RGB, HSL, and HSV

CSS-like color functions are also written as strings:

```akcss
Button.primary {
    Background: "rgb(51, 102, 153)";
    BorderBrush: "rgba(51, 102, 153, 0.5)";
    Foreground: "hsl(210, 50%, 40%)";
    Background: "hsla(210, 50%, 40%, 75%)";
    BorderBrush: "hsv(210, 67%, 60%)";
    Foreground: "hsva(210, 67%, 60%, 0.75)";
}
```

RGB components accept byte values or percentages. Alpha accepts a value from `0` to `1` or a percentage. HSL and HSV hue values are expressed in degrees; their remaining components use percentages.

When the target property is `IBrush`, AKCSS converts the parsed color to a `SolidColorBrush`. Other brush types can be supplied with regular C# expressions or resources:

```akcss
@using Avalonia.Media;

Button.primary {
    Background: Brushes.CornflowerBlue;
    BorderBrush: Amx.DynamicResource<IBrush>("ControlBorderBrush");
}
```

## CornerRadius

There is currently no dedicated AKCSS literal or tuple syntax for `CornerRadius`. Use a regular C# expression, including for a uniform radius:

```akcss
@using Avalonia;

Border.card {
    CornerRadius: new CornerRadius(8);
}
```

For individual corners, use the four-value constructor. The order is `topLeft, topRight, bottomRight, bottomLeft`:

```akcss
@using Avalonia;

Border.card {
    CornerRadius: new CornerRadius(8, 8, 0, 0);
}
```

Resources containing a `CornerRadius` are also supported:

```akcss
Border.card {
    CornerRadius: Amx.DynamicResource<CornerRadius>("--radius-md");
}
```

In `.akbura` markup, a plain `CornerRadius` attribute can use Avalonia's string parser:

```akbura
<Border CornerRadius="8,8,0,0" />
```

That markup literal conversion is separate from AKCSS value syntax.


## Resource expressions

Static and dynamic resources can participate in larger value expressions:

```akcss
Control.w-(double width) {
    Width: width * Amx.DynamicResource<double>("--spacing");
}
```

See [Markup Extensions](/akcss/markup-extensions) for resource lookup and generated binding behavior.

## Summary

| Value type | Concise syntax |
| --- | --- |
| Enums and static members | `Center`, `FontWeight.Bold`, `(FontWeight)700` |
| `Thickness` | `10`, `(16, 8)`, `(4, 8, 12, 16)`, `(horizontal: 16)` |
| `Color` / `IBrush` | `White`, `"#336699"`, `"rgb(...)"`, `"hsl(...)"`, `"hsv(...)"` |
| `CornerRadius` | regular C# such as `new CornerRadius(8)` or `new CornerRadius(8, 8, 0, 0)` |
| Other values | regular typed C# expressions |
