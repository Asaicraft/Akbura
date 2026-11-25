---
title: Native styles and control themes
summary: Generate Avalonia styles, themes, and control templates from AKCSS.
---

Standalone AKCSS files with native rules or named themes expose a C# facade. The project namespace and relative file path determine its name. For example, `Hello.akcss` in a project with root namespace `HelloWorld` produces `HelloWorld.Hello`.

Open the [Feature Gallery](https://asaicraft.github.io/Akbura/Gallery/akcss-themes/) to see two buttons sharing a template. In the [Playground](https://asaicraft.github.io/Akbura/Playground/), choose **AKCSS control themes** and open `ButtonThemes.akcss` to change a brush or selector and see the preview update.

## A button theme

Save the following as `Hello.akcss`.

```akcss
@using Avalonia;
@using Avalonia.Controls;
@using Avalonia.Controls.Presenters;
@using Avalonia.Media;

@mixin ContentPresenter DefaultContentPresenterTBind()
{
    @template-bind Background, BorderBrush, BorderThickness, CornerRadius,
        Content, ContentTemplate, Foreground, Padding,
        HorizontalContentAlignment, VerticalContentAlignment;
}

@theme Button MyTheme
{
    Background: Amx.DynamicResource("ButtonBackground");

    @template
    {
        <ContentPresenter
            x.Name="PART_ContentPresenter"
            RecognizesAccessKey={true}>
            @include DefaultContentPresenterTBind();
        </ContentPresenter>
    }

    ^:pointerover /template/ #PART_ContentPresenter
    {
        Background: Amx.DynamicResource("ButtonBackgroundPointerOver");
    }

    ^:pressed /template/ #PART_ContentPresenter
    {
        Background: Amx.DynamicResource("ButtonBackgroundPressed");
    }
}

(global::Avalonia.Controls.Border) > TextBlock
{
    FontSize: 14d;
}
```

Set the three brush resources on the window or another resource host above the button. Apply the theme with a C# expression in markup.

```akbura
using Avalonia.Controls;
using HelloWorld;

<Button Theme={Hello.MyTheme} Content="Hello" />
```

`global::HelloWorld.Hello.MyTheme` and `HelloWorld.Hello.MyTheme` also resolve in C# expressions. The theme property caches its definition. Avalonia builds a new presenter and name scope for each button. The presenter follows the button's properties through one-way template bindings. Native state styles override the template background while active.

## Share a template across themes

Add a typed mixin and a derived theme to `Hello.akcss`.

```akcss
@mixin Button SetButtonSurface(double radius, double horizontalPadding = 16d)
{
    CornerRadius: new CornerRadius(radius);
    Padding: new Thickness(horizontalPadding, 8);
    BorderThickness: new Thickness(1);
}

@theme Button AccentTheme
{
    @based-on MyTheme;
    @include SetButtonSurface(8d, 20d);
    Background: Brushes.RoyalBlue;
    BorderBrush: Brushes.CornflowerBlue;
    Foreground: Brushes.White;
}
```

Set `Theme={Hello.AccentTheme}` on a second button. It shares the template defined by `MyTheme` and applies its own colors and border. A mixin declares its receiver type before its name. `SetButtonSurface` sets properties on a `Button` and declares a default for its padding parameter. `DefaultContentPresenterTBind` runs inside a `ContentPresenter` and binds each listed property to the same-named property on the templated button.

For `^:pointerover /template/ #PART_ContentPresenter`, `^` starts at the themed button, `:pointerover` checks the button's state, and `/template/` reaches the element named by `x.Name`. The setter updates the presenter's background while that selector matches.

## Edit themes and mixins

The editor offers AKCSS directives in rule bodies and switches to markup completion inside `@template`. In a selector such as `^ /template/ ContentPresenter#PART_ContentPresenter`, property completion uses `ContentPresenter`. Hover over `@template-bind` entries to see the templated parent's source property and the element's destination property.

Signature help on a mixin call shows the declared receiver, arguments, and defaults. Go to Definition follows includes, source-theme inheritance, and known template parts to their original declarations. C# accesses to an exported theme also navigate to the original AKCSS theme.

Rename supports source mixins, mixin parameters, local themes, and unambiguous template parts. Exported themes require cross-language rename because ordinary C# files can refer to them. Template-binding shorthand denotes two endpoints and uses the destination for Go to Definition.

## Install native rules

```csharp
host.Styles.Add(HelloWorld.Hello.CreateStyles());
```

Every call creates a fresh style collection. Native rules can combine predicates and relationships, as in `Border.frame.card` or `Border.frame > TextBlock`. Standalone imports make declarations available for lookup. A component installs general native rules from its inline `@akcss` blocks on its own `Styles` collection during initialization.

A base theme can also be an existing C# `ControlTheme`. The derived theme can supply its own `@template`. Inside each native style or theme, the final setter for each Avalonia property wins after includes expand. Construction-time values and include arguments are evaluated in declaration order.

Native setter values are evaluated during style or theme construction. Dynamic resource bindings resolve on the matched control. Receiver-dependent native setter expressions, context-free static resource setters, and reactive AKCSS `@if` in native bodies produce generation diagnostics. Mixin bodies are available from source in this stage. Restart the application to apply edits to native style and theme trees during development.

See [Native fragments in utilities and classes](./native-application-fragments) for application-owned styles.
