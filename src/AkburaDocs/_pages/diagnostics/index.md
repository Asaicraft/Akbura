---
title: Diagnostics
summary: Connect Akbura diagnostics, open the developer window, and inspect component data while an application is running.
order: 0
---

[![NuGet](https://img.shields.io/nuget/vpre/Akbura.Diagnostics?logo=nuget&label=NuGet)](https://www.nuget.org/packages/Akbura.Diagnostics)

Run the application in Debug configuration and press the shortcut configured for Akbura diagnostics. This opens the Akbura component inspector independently of Avalonia Developer Tools.

## Add the package

Applications created with the `akbura.app` template already include diagnostics
in Debug builds. No additional installation is required for those projects.

For an existing Akbura application, install the package from NuGet:

:::sh
dotnet add package Akbura.Diagnostics --version 12.0.4-alpha.2
:::

Because diagnostics is a development tool, keep its assets private and reference
it only in Debug builds. The resulting project entry should be:

```xml
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
    <PackageReference Include="Akbura.Diagnostics"
                      Version="12.0.4-alpha.2"
                      PrivateAssets="all" />
</ItemGroup>
```

`PrivateAssets="all"` prevents the development dependency from flowing to projects
that reference your application or library. The Debug condition keeps diagnostics
out of Release restore, build, and publish output.

## Attach diagnostics

Attach the diagnostics window from `Application.Initialize()` after the application resources have been loaded:

```csharp
using Akbura.Diagnostics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        this.AttachAkburaDevTools();
#endif
    }
}
```

The default toggle gesture is `F12`.

Use the options overload to choose another gesture. The Feature Gallery uses `Ctrl+F12`:

```csharp
#if DEBUG
this.AttachAkburaDevTools(options =>
{
    options.ToggleGesture = new KeyGesture(
        Key.F12,
        KeyModifiers.Control);
});
#endif
```

Keep the call inside `#if DEBUG` so diagnostics is not attached in Release builds.

## Use Akbura and Avalonia developer tools together

Akbura diagnostics is not a replacement for Avalonia Developer Tools. The two tools inspect the application from different perspectives and are intended to complement each other.

Avalonia Developer Tools works with Avalonia's visual and logical trees. It is useful for inspecting controls, styles, bindings, layout, properties, and routed events.

Akbura diagnostics works with the Akbura component tree. It shows the component hierarchy and exposes component-specific data such as states, parameters, and injected services.

An Akbura component also participates in Avalonia's visual and logical trees, so the same component may be visible in both tools:

- use Avalonia Developer Tools to inspect how the component is represented and rendered by Avalonia;
- use Akbura diagnostics to inspect the component itself and its Akbura state.

Both tools can be attached to the same application:

```csharp
public override void Initialize()
{
    AvaloniaXamlLoader.Load(this);

#if DEBUG
    this.AttachDeveloperTools();

    this.AttachAkburaDevTools(options =>
    {
        options.ToggleGesture = new KeyGesture(
            Key.F12,
            KeyModifiers.Control);
    });
#endif
}
```

In this example, Avalonia Developer Tools keeps its own shortcut, while Akbura diagnostics opens with `Ctrl+F12`. 

## Use the diagnostics window

Run the application in Debug configuration and press the configured shortcut.

The diagnostics window lets you:

- browse the live Akbura component tree;
- inspect states, parameters, and injected services;
- edit values with the available input builders;
- apply a draft value or reload the current value from the component;
- switch to another compatible input builder from the editor selector.

Built-in input builders cover strings, numeric values, collections, and a universal fallback. The universal input accepts values supported by a .NET `TypeConverter`, a public `Parse` or `TryParse` method, or a JSON representation.

::: info
Akbura diagnostics currently supports the classic desktop application lifetime.
:::

## Configure application services

A custom input builder can receive application services through `InputRequest.Services`:

```csharp
this.AttachAkburaDevTools(options =>
{
    options.Services = serviceProvider;
});
```

Use this only when an editor needs application-specific data, such as a catalog of valid values.

## Next step

Continue with [Custom input builders](diagnostics/custom-input) to add a specialized editor and give it priority over the built-in inputs.
