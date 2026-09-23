# Why these ViewModels live in a separate project

Akbura's compiler, BlackSilence, is a Roslyn incremental C# source generator. It uses C# symbols to validate typed bindings and generate compiled binding paths.

CommunityToolkit.Mvvm also uses source generators. Attributes such as `[ObservableProperty]` and `[RelayCommand]` add properties and commands to the completed ViewModel type. During ordinary source generation, one generator cannot rely on seeing another generator's normal output from the same compilation. A binding to a generated command can therefore be unresolved when Akbura analyzes UI in that same project.

This template avoids that dependency by putting the ViewModel declarations and their generation in this class library. The UI project references the library through `ProjectReference`. The library's public generated members are then available as part of the referenced API when the UI is built.

```text
AkburaXplatTemplate shared UI project with .akbura files
    -> AkburaXplatTemplate.ViewModels library with generated properties and commands
```

This means separate application projects, not separate copies of the generator implementation. Creating another folder inside the UI project would not establish the required compilation boundary.

## Where code belongs

Keep generator-backed ViewModels here, together with their platform-neutral models and service contracts. Keep views, UI resources, AppShell, platform lifetimes, and DI registration in the UI and platform projects. Keep the dependency one-way: this library must not reference the UI.

Do not link or copy these ViewModel source files back into the UI project. Keep all parts of an annotated partial class in this project. Members consumed by typed UI bindings must be publicly accessible. Add real model types only when needed; a Models folder is not required for this pattern.

The CommunityToolkit variant deliberately includes both annotated partial properties and a field-backed `[ObservableProperty]`. `UserName` may store `null`; `GreetingService` supplies the user-facing fallback.

## Building and editing

Build the application's Desktop entry project normally. Its project reference builds this dependency; a manual two-stage build and a second build attempt are not required.

```bash
dotnet build AkburaXplatTemplate.Desktop/AkburaXplatTemplate.Desktop.csproj
```

Use the generated public properties and commands in `.akbura` bindings. `x.DataType` describes the binding type; it does not instantiate the ViewModel or assign runtime DataContext. Existing application composition still supplies the ViewModel.

An editor may need a project refresh or build after generated API changes. Successful editor completion is not a substitute for a clean command-line build. Do not commit generated `.g.cs` files, read stale files from `obj`, reorder packages to try to order generators, or replace compiled bindings with reflection bindings to bypass this boundary.

This limitation concerns generated members Akbura must inspect, not every possible source generator. Independent generators can coexist without this dependency. The same library boundary can be used for other generators that produce public models or members needed by the UI.

## References

- [Roslyn incremental generator cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md)
- [CommunityToolkit.Mvvm ObservableProperty generator](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/observableproperty)
- [CommunityToolkit.Mvvm RelayCommand generator](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/relaycommand)
