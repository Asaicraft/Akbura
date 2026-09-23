# Akbura cross-platform application

`akbura.xplat` creates a shared Akbura MVVM UI, one sibling platform-neutral ViewModels library, and separate Desktop, Browser, Android and iOS hosts. Edit `AkburaRawProjectNamePlaceholder/Views/AppShell.akbura` to change the application shell, tabs, drawer, or navigation pages. `MainView.akbura` remains the shared Home content, while `MainViewModel` owns the counter, greeting and commands.

The composition root owns one `MainViewModel` and assigns it to both the shell's required `Vm` property and its outer `DataContext`. `Vm` satisfies dependency injection; `DataContext={Vm}` supplies the runtime binding context, and `x.DataType` only checks binding paths at compile time. Provider-enabled variants still pass `Vm` explicitly, while a directly created shell can resolve it from the configured provider. Every Activity factory invocation creates a fresh `AppShell` and control tree for the application-owned ViewModel.

`akbura.app` demonstrates local state/hooks and components, `akbura.mvvm` demonstrates desktop MVVM/native bindings, and `akbura.xplat` uses the same MVVM model for shared cross-platform UI. Use `${Binding ...}` for ViewModel properties and commands in `.akbura`; `{Message}` in `GreetingCard` reads its component parameter. The selected `AppShell.akbura` declares real Avalonia page objects rather than hiding the UI tree in a C# factory.

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
| `--akbura-version` / `-akv` | `12.0.4-alpha.9` | compatible Akbura version |

For `TabbedPage`, the shell declares `using PageList = Avalonia.Collections.AvaloniaList<Avalonia.Controls.Page>;`. This alias supplies one concrete mutable `AvaloniaList<Page>` value to the native `IEnumerable<Page>` property; its children are real `ContentPage` objects, not generated wrapper controls or a custom `PageList` type.

`--cpm true` uses `Directory.Packages.props`; `--cpm false` uses `Directory.Build.props`, pins matching versions on each `PackageReference`, and disables inherited CPM imports. `--remove-view-locator true` removes the optional typed ViewLocator and its registration; views are still explicitly composed by the app. The page-type choice emits only its selected shell. `PageNavigationHost` provides safe-area and system Back integration for Page modes. The `.akbura` view remains a `Control`, so each Page mode wraps it in a real Avalonia `Page`.

## Build and run

Creating this template does not restore packages or install workloads. Build only the host you need:

```bash
dotnet restore AkburaRawProjectNamePlaceholder.Desktop/AkburaRawProjectNamePlaceholder.Desktop.csproj
dotnet run --project AkburaRawProjectNamePlaceholder.Desktop/AkburaRawProjectNamePlaceholder.Desktop.csproj
```

The Desktop host reaches the shared UI and `AkburaRawProjectNamePlaceholder.ViewModels` through ordinary project references. No manual producer build or second application build is required. See [`AkburaRawProjectNamePlaceholder.ViewModels/ViewModels/README.md`](AkburaRawProjectNamePlaceholder.ViewModels/ViewModels/README.md) for the source-generator boundary and editing rules.

For Browser, install the .NET 10 `wasm-tools` workload and run:

```bash
dotnet restore AkburaRawProjectNamePlaceholder.Browser/AkburaRawProjectNamePlaceholder.Browser.csproj
dotnet run --project AkburaRawProjectNamePlaceholder.Browser/AkburaRawProjectNamePlaceholder.Browser.csproj
```

Android requires the .NET Android workload and a configured emulator or device. iOS requires a Mac with Xcode and the matching .NET iOS workload. Build each host separately, for example `dotnet build AkburaRawProjectNamePlaceholder.Android/AkburaRawProjectNamePlaceholder.Android.csproj`. Do not build the whole solution on a machine without all platform workloads. This template does not install workloads or run `dotnet restore` on the whole solution for you.

In Desktop Debug, F12 opens Avalonia Developer Tools and Ctrl+F12 opens Akbura Diagnostics. Those keyboard shortcuts and inspectors are not advertised as mobile/browser debugging features. Release builds have no diagnostics package references. Browser/mobile teardown is platform-specific; only the owned Microsoft DI container is disposed on Desktop application exit, not on Activity detach.

The project's package defaults must match the installed Akbura/Avalonia versions. A parent MSBuild configuration can still influence unrelated settings; generate in a clean output directory when diagnosing a build.
