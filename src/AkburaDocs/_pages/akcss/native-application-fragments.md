---
title: Native fragments in utilities and classes
summary: Style template parts and descendants with an AKCSS utility or named class.
---

A utility or named class with a nested selector or include applies its complete body as an Avalonia style tree. The application is scoped to the individual control. A rule can match its descendants and template parts using normal Avalonia selectors.

## Style a Fluent Button presenter

```akcss
@using Avalonia.Controls;
@using Avalonia.Controls.Presenters;

@mixin Button PresenterBackground(object resourceKey)
{
    ^ /template/ ContentPresenter#PART_ContentPresenter
    {
        Background: Amx.DynamicResource(resourceKey);
    }
}

@utilities
{
    Button.bg-red-300
    {
        @include PresenterBackground("--color-red-300");
    }

    Button.bg-red-700
    {
        @include PresenterBackground("--color-red-700");
    }
}
```

Define both brush resources on a resource host above the button. Import the AKCSS module and apply the utilities using the existing markup syntax.

```akbura
using Avalonia.Controls;
using Akbura.Markup;
using MyApp.Fragments.akcss;

<StackPanel>
    <Button Content="First" bg-red-300 ${hover}:bg-red-700 />
    <Button Content="Unchanged" />
</StackPanel>
```

The utility tracks the current template presenter. A resource change updates the active setter. Leaving hover restores the lower-ranked application. Removing the applications exposes the remaining native value or binding.

The explicit `ContentPresenter` type sets the property-binding context for the part. The utility can apply to eligible derived buttons. The application anchor identifies the instance receiving the utility, and a nested `^` anchors to its enclosing style.

## Apply a named class

```akcss
Button.interactive
{
    Opacity: 0.8d;

    ^:pointerover
    {
        Opacity: 1d;
    }
}
```

```akbura
<Button Content="Interactive" class="interactive" />
```

Root and nested setters share the same native tree and participate at style-trigger priority. Local values and animations have higher priority. Plain AKCSS declarations continue to use their scalar cascade, so conflicts between a plain declaration and a native fragment follow Avalonia priorities.

## Order applications

Unprefixed applications follow their order in markup. Ungrouped conditions with the `SourceOrder` policy share that order. A single named prefix group can sit uniformly above or below unprefixed applications. Inside that group, its declared order comes first, followed by markup order. `${hover}` and `${focus}` share the interaction group, with focus ranked higher.

Each matching control receives its own contributions. An application with a higher rank on one part leaves lower-ranked rules available on other parts.

## Generation boundaries

Use `@include` to compose native bodies. A contextual `@apply` reports a generation error. Direct properties, receiver-dependent C# values, context-free static resources, reactive AKCSS `@if` in a native body, and incompatible prefix policies also produce errors.

Compiled modules expose their native bodies through their existing utility and class definitions. Source mixins expand in the producing project. Native application changes use full replacement for Hot Reload. Restart the application to apply edits to shared native style and theme definitions.
