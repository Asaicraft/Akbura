# Akbura desktop MVVM app

This project pairs Akbura `.akbura` views and AKCSS with an Avalonia desktop host. `MainViewModel` owns the counter, editable name, dependent greeting and commands. `App` creates one ViewModel and assigns it as the window's runtime `DataContext`; `x.DataType="MainViewModel"` in `MainView.akbura` gives the compiler the binding type but does not create that context.

The view uses `${Binding CountText}`, `${Binding UserName, Mode=TwoWay}` and command bindings. `GreetingCard` receives `${Binding Greeting}` as a component parameter, then renders its parameter with a C# expression. The ViewModel source uses the selected CommunityToolkit.Mvvm or ReactiveUI implementation. The optional `ViewLocator` is a typed DataTemplate for `MainViewModel`; startup uses explicit composition and works without it.

## Run

```bash
dotnet restore AkburaMvvmTemplate.csproj
dotnet run --project AkburaMvvmTemplate.csproj
```

`akbura.app` demonstrates local state/hooks, `akbura.mvvm` demonstrates native MVVM bindings, and `akbura.xplat` carries the same MVVM idea into Desktop, Browser, Android and iOS hosts.

## Template choices

`--mvvm` selects `CommunityToolkit` (default) or `ReactiveUI`. `--di` selects `None` (default), `Microsoft.Extensions.DependencyInjection`, or `Splat.Locator`, independently of the toolkit. `--cpm false` (default) stores package versions in `Directory.Build.props` and disables inherited central package management; `--cpm true` emits `Directory.Packages.props` and version-free project references. `--remove-view-locator true` removes the optional DataTemplate. `--no-restore` skips the automatic restore performed by `dotnet new`.

The project targets .NET 10 and uses Avalonia `AvaloniaVersionTemplateParameter` with Akbura `AkburaVersionTemplateParameter` by default. Changing either version requires a compatible package pair. A parent repository's MSBuild props/targets may still affect generated projects; generate outside another repository when comparing options.

In Desktop Debug, F12 opens Avalonia Developer Tools and Ctrl+F12 opens Akbura Diagnostics. Release has neither diagnostics package or hook. The Splat and Microsoft DI branches keep one application-level ViewModel and service provider; Microsoft DI is disposed when the desktop process lifetime returns. Design-time initialization does not require external configuration or network access.
