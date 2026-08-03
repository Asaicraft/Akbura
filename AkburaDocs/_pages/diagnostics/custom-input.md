---
title: Custom input builders
summary: Create and register an application-specific diagnostics editor using InputBuilder and InputValueProperty.
order: 10
---

A custom input builder replaces free-form value entry with an editor designed for a specific component value. This example adds a `Route` input for the Feature Gallery's `Url` state and presents the known routes in a drop-down list.

## How input builders are selected

Diagnostics evaluates `AkburaDiagnosticsOptions.InputBuilders` in order. Every builder whose `CanProvide()` method returns `true` appears in the selector, and the first compatible builder is selected initially.

Insert an application-specific builder at index `0` when it should be preferred over the built-in editors. The universal editor remains available as the final fallback.

## Create `RouteInputBuilder`

Create this file:

```text
Akbura.FeatureGallery/Akbura.FeatureGallery/Diagnostics/RouteInputBuilder.cs
```

Add the following implementation:

```csharp
using Akbura.Diagnostics;
using FeatureGalleryComponent = Akbura.FeatureGallery.Components.App;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;

namespace Akbura.FeatureGallery.Diagnostics;

public sealed class RouteInputBuilder : InputBuilder
{
    public override Type OutputType => typeof(string);

    public override bool CanProvide(InputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.EditorType == typeof(string) &&
               request.ComponentInstance is Router &&
               string.Equals(
                   request.MemberName,
                   nameof(Router.Url),
                   StringComparison.Ordinal);
    }

    protected override Control BuildCore(InputRequest request)
    {
        return BuildCore(
            request,
            request.ExistingValue);
    }

    protected override Control BuildCore(
        InputRequest request,
        object? existingValue)
    {
        var router = (Router)request.ComponentInstance!;
        var routes = GetRoutes(
            router,
            existingValue as string);

        var comboBox = new ComboBox
        {
            ItemsSource = routes,
            MinWidth = 180d,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var binding = new CompiledBinding(InputValueBindingPath)
        {
            Source = comboBox,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger =
                UpdateSourceTrigger.PropertyChanged,
        };

        comboBox.Bind(
            SelectingItemsControl.SelectedItemProperty,
            binding);

        return comboBox;
    }

    private static IReadOnlyList<string> GetRoutes(
        Router router,
        string? currentRoute)
    {
        var routes = new List<string>();
        var knownRoutes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var pages = router.Pages;

        if (pages is not null)
        {
            foreach (var page in pages)
            {
                var route = page.Url;

                if (string.IsNullOrWhiteSpace(route) ||
                    !knownRoutes.Add(route))
                {
                    continue;
                }

                routes.Add(route);
            }
        }

        if (!string.IsNullOrWhiteSpace(currentRoute) &&
            knownRoutes.Add(currentRoute))
        {
            routes.Add(currentRoute);
        }

        return routes;
    }
}
```

`OutputType` declares the value produced by the editor. `CanProvide()` limits the editor to the Feature Gallery `App` component's `Url` member. `BuildCore()` binds the selected route to `InputBuilder.InputValueProperty`, which is the value contract used by the diagnostics UI.

The result is a route-specific editor instead of a plain text field:

![The Route input builder editing the Url state.](img/diagnostics/custom-route-input.png)
![The input selector showing routes.](img/diagnostics/custom-route-input-selector.png)

## Add the input icon

Diagnostics looks up an icon using the builder's fully qualified type name followed by `.Icon`. For this class, the resource key must be:

```text
Akbura.FeatureGallery.Diagnostics.RouteInputBuilder.Icon
```

Add the geometry inside `Application.Resources` in:

```text
Akbura.FeatureGallery/Akbura.FeatureGallery/App.axaml
```

Place it in the existing `ResourceDictionary`, after `ResourceDictionary.MergedDictionaries` and before the closing `</ResourceDictionary>`:

```xml
<StreamGeometry x:Key="Akbura.FeatureGallery.Diagnostics.RouteInputBuilder.Icon">
    M11,2
    H13
    V5
    H19
    L22,8
    L19,11
    H13
    V22
    H11
    V15
    H5
    L2,12
    L5,9
    H11
    Z

    M13,7
    V9
    H18.17
    L19.17,8
    L18.17,7
    Z

    M5.83,11
    L4.83,12
    L5.83,13
    H11
    V11
    Z
</StreamGeometry>
```

The relevant part of `App.axaml` should look like this:

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceInclude Source="avares://Akbura/Styles.axaml" />
        </ResourceDictionary.MergedDictionaries>

        <StreamGeometry x:Key="Akbura.FeatureGallery.Diagnostics.RouteInputBuilder.Icon">
            <!-- geometry from the previous snippet -->
        </StreamGeometry>
    </ResourceDictionary>
</Application.Resources>
```

## Register the builder

Update `App.axaml.cs` to import the builder namespace and Avalonia input types:

```csharp
using Akbura.Diagnostics;
using Akbura.FeatureGallery.Diagnostics;
using Avalonia.Input;
```

Register the builder while attaching diagnostics:

```csharp
#if DEBUG
this.AttachDeveloperTools();
this.AttachAkburaDevTools(options =>
{
    options.ToggleGesture = new KeyGesture(
        Key.F12,
        KeyModifiers.Control);

    options.InputBuilders.Insert(
        0,
        new RouteInputBuilder());
});
#endif
```

Index `0` gives `RouteInputBuilder` priority. The selector still lists every other compatible builder, including `Universal`:

![The input selector showing Route and Universal builders.](img/diagnostics/custom-route-selector.png)

The second `Url` value in the screenshot belongs to another component. Because `CanProvide()` restricts the custom builder to the Feature Gallery `App` component, that value continues to use the universal editor.

## Input builder contract

A custom builder must follow four rules:

1. Return the edited value type from `OutputType`.
2. Return `true` from `CanProvide()` only for values the editor can safely handle.
3. Return an Avalonia `Control` from `BuildCore()`.
4. Keep the control's editable property synchronized with `InputBuilder.InputValueProperty` using a two-way binding.

Diagnostics owns Apply, Reload, validation display, and writing the committed value back to the component.

## Using application services

Set `options.Services` when a builder needs data from the application:

```csharp
this.AttachAkburaDevTools(options =>
{
    options.Services = serviceProvider;
    options.InputBuilders.Insert(0, new RouteInputBuilder());
});
```

The builder can read those services from `request.Services` in `CanProvide()` or `BuildCore()`.

## Troubleshooting

When the builder does not appear, verify that it was inserted before diagnostics was attached and that `CanProvide()` matches the actual `ComponentType`, `MemberName`, and `EditorType`.

When the icon is missing, verify that the resource key exactly matches the builder's namespace and class name, including the `.Icon` suffix.

When the editor changes visually but Apply receives the old value, verify the two-way binding to `InputBuilder.InputValueProperty`.
