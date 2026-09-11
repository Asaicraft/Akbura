# Akbura Templates

Templates for creating Avalonia desktop applications and components with Akbura.

## Install

```powershell
dotnet new install Akbura.Templates
```

## Create an application

```powershell
dotnet new akbura.app -n MyApp
```

Choose an optional dependency-injection provider with `--di`:

```powershell
dotnet new akbura.app -n MyApp --di Microsoft.Extensions.DependencyInjection
dotnet new akbura.app -n MyApp --di Splat.Locator
```

The default is `None`, which adds no external DI package or service infrastructure.

## Create a component

Run the item template from an existing Akbura project:

```powershell
dotnet new akbura.component -n ProfileCard --namespace MyApp.Components -o Components
```

To create a component together with a C# code-behind partial class, use:

```powershell
dotnet new akbura.partial-component -n ProfileCard --namespace MyApp.Components -o Components
```

This creates `ProfileCard.akbura` and `ProfileCard.akbura.cs`.

The application template demonstrates reactive state, AKCSS utilities, responsive
breakpoints, reusable components, and optional service injection. Debug builds include
Avalonia Developer Tools on `F12` and the Akbura component inspector on `Ctrl+F12`;
diagnostic dependencies are excluded from Release builds.

The templates target Akbura `12.0.4-alpha.4` by default.
