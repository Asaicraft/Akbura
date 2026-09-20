# Akbura cross-platform application

`akbura.xplat` creates a shared Akbura MVVM UI and separate Desktop, Browser, Android and iOS hosts. The main screen is `AkburaRawProjectNamePlaceholder/Views/MainView.akbura`; `MainViewModel` owns the counter, greeting and commands. The app-level composition root creates one view model and assigns the runtime `DataContext`. `x.DataType` checks binding paths at compile time; it does not create a `DataContext`.

`akbura.app` demonstrates local state/hooks and components, `akbura.mvvm` demonstrates desktop MVVM/native bindings, and `akbura.xplat` uses the same MVVM model for shared cross-platform UI. Use `${Binding ...}` for ViewModel properties and commands in `.akbura`; `{Message}` in `GreetingCard` reads its component parameter. Ordinary `.axaml` shells use Avalonia XAML syntax.

## Choices

| Option | Default | Values |
| --- | --- | --- |
| `--framework` / `-f` | `net10.0` | `net10.0` |
| `--mvvm` / `-m` | `CommunityToolkit` | `CommunityToolkit`, `ReactiveUI` |
| `--main-view-page-type` / `-page` | `None` | `None`, `ContentPage`, `TabbedPage`, `DrawerPage`, `NavigationPage` |
| `--di` | `None` | `None`, `Microsoft.Extensions.DependencyInjection`, `Splat.Locator` |
| `--cpm` / `-cpm` | `true` | `true`, `false` |
| `--remove-view-locator` / `-rvl` | `false` | `true`, `false` |
| `--avalonia-version` / `-av` | `12.0.4` | compatible Avalonia version |
| `--akbura-version` / `-akv` | `12.0.4-alpha.6` | compatible Akbura version |

`--cpm true` uses `Directory.Packages.props`; `--cpm false` uses `Directory.Build.props`, pins matching versions on each `PackageReference`, and disables inherited CPM imports. `--remove-view-locator true` removes the optional typed ViewLocator and its registration; views are still explicitly composed by the app. The page-type choice emits only its selected shell. `PageNavigationHost` provides safe-area and system Back integration for Page modes. The `.akbura` view remains a `Control`, so each Page mode wraps it in a real Avalonia `Page`.

## Build and run

Creating this template does not restore packages or install workloads. Build only the host you need:

```bash
dotnet restore AkburaRawProjectNamePlaceholder.Desktop/AkburaRawProjectNamePlaceholder.Desktop.csproj
dotnet run --project AkburaRawProjectNamePlaceholder.Desktop/AkburaRawProjectNamePlaceholder.Desktop.csproj
```

For Browser, install the .NET 10 `wasm-tools` workload and run:

```bash
dotnet restore AkburaRawProjectNamePlaceholder.Browser/AkburaRawProjectNamePlaceholder.Browser.csproj
dotnet run --project AkburaRawProjectNamePlaceholder.Browser/AkburaRawProjectNamePlaceholder.Browser.csproj
```

Android requires the .NET Android workload and a configured emulator or device. iOS requires a Mac with Xcode and the matching .NET iOS workload. Build each host separately, for example `dotnet build AkburaRawProjectNamePlaceholder.Android/AkburaRawProjectNamePlaceholder.Android.csproj`. Do not build the whole solution on a machine without all platform workloads. This template does not install workloads or run `dotnet restore` on the whole solution for you.

In Desktop Debug, F12 opens Avalonia Developer Tools and Ctrl+F12 opens Akbura Diagnostics. Those keyboard shortcuts and inspectors are not advertised as mobile/browser debugging features. Release builds have no diagnostics package references. Browser/mobile teardown is platform-specific; only the owned Microsoft DI container is disposed on Desktop application exit, not on Activity detach.

The project's package defaults must match the installed Akbura/Avalonia versions. A parent MSBuild configuration can still influence unrelated settings; generate in a clean output directory when diagnosing a build.
