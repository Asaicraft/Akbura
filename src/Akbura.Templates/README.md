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

The application template demonstrates reactive state, AKCSS utilities, responsive breakpoints, reusable components, and optional service injection.

> The development template currently targets Akbura `12.0.4-template.1`. Replace the default with the tested public package version before publishing `Akbura.Templates`.
