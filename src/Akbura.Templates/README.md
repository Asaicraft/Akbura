# Akbura Templates

Project and component templates for Avalonia applications built with Akbura.

## Install

```powershell
dotnet new install Akbura.Templates
```

For a local package, install its `.nupkg` instead:

```powershell
dotnet new install ./artifacts/Akbura.Templates.VERSION.nupkg
```

## Choose a template

| Short name | Output | Starting point |
| --- | --- | --- |
| `akbura.app` | Desktop project | Local `state`/hooks, AKCSS, and components. |
| `akbura.mvvm` | Desktop project | MVVM, native bindings, commands, and an Akbura view. |
| `akbura.xplat` | Five-project solution | Shared MVVM UI with Desktop, Browser, Android, and iOS hosts. |
| `akbura.component` | Item | A `.akbura` component for an existing project. |
| `akbura.partial-component` | Item | A `.akbura` component and matching `.akbura.cs` partial class. |

The existing `akbura.app` remains the local-state example. The two MVVM templates
demonstrate a `MainViewModel` with `INotifyPropertyChanged` and commands. Their
`MainView.akbura` uses `${Binding ...}`; `x.DataType` identifies the binding type,
while the application composition root supplies the actual runtime `DataContext`.
An ordinary `{expression}` in a component is C#, not an MVVM binding.

## Create a local-state desktop app

```powershell
dotnet new akbura.app -n MyApp
dotnet new akbura.app -n MyAppWithDI --di Microsoft.Extensions.DependencyInjection
dotnet new akbura.app -n MyAppWithSplat --di Splat.Locator
```

The default `--di None` adds no external container. This template demonstrates
local `state`, responsive AKCSS utilities, and reusable components rather than
the ViewModel/command design used by the two templates below.

## Create a desktop MVVM app

```powershell
dotnet new akbura.mvvm -n DemoDesktop
dotnet run --project DemoDesktop/DemoDesktop.csproj
```

The template restores packages automatically. Use `--no-restore` to defer that work:

```powershell
dotnet new akbura.mvvm -n DemoReactive `
    --mvvm ReactiveUI `
    --di Microsoft.Extensions.DependencyInjection `
    --cpm true `
    --remove-view-locator true `
    --no-restore
dotnet restore DemoReactive/DemoReactive.csproj
```

## Create a cross-platform solution

```powershell
dotnet new akbura.xplat -n DemoCross
dotnet restore DemoCross/DemoCross.Desktop/DemoCross.Desktop.csproj
dotnet run --project DemoCross/DemoCross.Desktop/DemoCross.Desktop.csproj
```

`akbura.xplat` does not automatically restore the whole solution or install
platform workloads. Build only the host supported by the current machine:

```powershell
dotnet build DemoCross/DemoCross.Browser/DemoCross.Browser.csproj
dotnet build DemoCross/DemoCross.Android/DemoCross.Android.csproj
dotnet build DemoCross/DemoCross.iOS/DemoCross.iOS.csproj
```

Browser requires the .NET WebAssembly workload; Android requires the Android SDK
and .NET Android workload; iOS requires a compatible macOS/Xcode and .NET iOS
workload. Do not build the complete solution on a machine lacking mobile workloads.
Browser/mobile debugging does not offer the desktop-only keyboard inspector UI.

To choose a page shell and another toolkit:

```powershell
dotnet new akbura.xplat -n DemoMobile `
    --mvvm ReactiveUI `
    --di Splat.Locator `
    --cpm false `
    --main-view-page-type NavigationPage `
    --remove-view-locator true
```

## Application options

| Option | Values | Desktop MVVM default | Cross-platform default |
| --- | --- | --- | --- |
| `-f`, `--framework` | `net10.0` | `net10.0` | `net10.0` |
| `-m`, `--mvvm` | `CommunityToolkit`, `ReactiveUI` | `CommunityToolkit` | `CommunityToolkit` |
| `-av`, `--avalonia-version` | Avalonia package version | Matched Avalonia version | Matched Avalonia version |
| `-akv`, `--akbura-version` | Akbura package version | Package release version | Package release version |
| `-rvl`, `--remove-view-locator` | `true`, `false` | `false` | `false` |
| `-cpm`, `--cpm` | `true`, `false` | `false` | `true` |
| `--di` | `None`, `Microsoft.Extensions.DependencyInjection`, `Splat.Locator` | `None` | `None` |
| `--no-restore` | Switch | Restore runs | Not available; no automatic restore |
| `-page`, `--main-view-page-type` | `None`, `ContentPage`, `TabbedPage`, `DrawerPage`, `NavigationPage` | Not available | `None` |

At the recorded source baseline, the defaults were Avalonia `12.0.4` and
Akbura `12.0.4-alpha.6`. Release automation stamps all application templates
with the versions actually packed. Inspect `dotnet new akbura.mvvm --help` or
`dotnet new akbura.xplat --help` for defaults of the installed package.

`--mvvm` selects the ViewModel implementation; `--di` independently selects how
the application constructs and owns its services. `--di None` needs no container.
`--cpm true` writes a root `Directory.Packages.props` and versionless project
references; `--cpm false` writes shared versions in `Directory.Build.props` and
disables inherited central package management. `--remove-view-locator true`
removes the optional ViewLocator but keeps MVVM and the chosen page shell.
Generated projects do not change a parent directory's MSBuild configuration.

## Add a component

Run these item templates from an existing Akbura project:

```powershell
dotnet new akbura.component -n ProfileCard --namespace MyApp.Components -o Components
dotnet new akbura.partial-component -n ProfileCard --namespace MyApp.Components -o Components
```

The partial-component template creates `ProfileCard.akbura` and
`ProfileCard.akbura.cs`. Debug desktop builds can use Avalonia Developer Tools on
`F12` and the Akbura component inspector on `Ctrl+F12`; diagnostic dependencies
are excluded from Release builds.

Template parity and update notes: [CONTRIBUTING.md](https://github.com/Asaicraft/Akbura/blob/master/src/Akbura.Templates/CONTRIBUTING.md).
