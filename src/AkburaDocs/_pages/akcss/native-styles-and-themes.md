---
title: Native styles and control themes
summary: Generate Avalonia styles, themes, and control templates from AKCSS.
---

Standalone AKCSS files with native rules or named themes expose a C# facade. The project namespace and relative file path determine its name. For example, `Hello.akcss` in a project with root namespace `HelloWorld` produces `HelloWorld.Hello`.

## A button theme

Save the following as `Hello.akcss`.

```akcss
@using Avalonia.Controls;
@using Avalonia.Controls.Presenters;

@mixin ContentPresenter DefaultContentPresenterTBind()
{
    @template-bind Background, BorderBrush, BorderThickness, CornerRadius,
        Content, ContentTemplate, Padding,
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

## Install native rules

```csharp
host.Styles.Add(HelloWorld.Hello.CreateStyles());
```

Every call creates a fresh style collection. Native rules can combine predicates and relationships, as in `Border.frame.card` or `Border.frame > TextBlock`. Standalone imports make declarations available for lookup. A component installs general native rules from its inline `@akcss` blocks on its own `Styles` collection during initialization.

Use `@based-on` to inherit a source theme or an existing C# `ControlTheme`. A derived theme inherits the base template until it declares a local `@template`. Inside each native style or theme, the final setter for each Avalonia property wins after includes expand. Construction-time values and include arguments are evaluated in declaration order.

Native setter values are evaluated during style or theme construction. Dynamic resource bindings resolve on the matched control. Receiver-dependent native setter expressions, context-free static resource setters, reactive AKCSS `@if` in native bodies, and nested fragments activated by legacy classes or utilities produce generation diagnostics. Mixin bodies are available from source in this stage. Restart the application to apply edits to native style and theme trees during development.
