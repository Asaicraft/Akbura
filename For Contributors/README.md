# For Contributors

Build with .NET SDK 10.0.400 or later; `global.json` selects a stable .NET 10 SDK.
Roslyn compiler and workspace packages share `AkburaRoslynVersion` in `Directory.Packages.props`.
The Visual Studio extension requires Visual Studio 2026 18.9 or later. Its public
VS adapter packages are downloaded only as compile-time references because their
NuGet dependency graphs are pinned to Roslyn 4.14; the IDE supplies their runtime assemblies.

If you want to add your own syntax, start with [Make Your Own Syntax.md](Make%20Your%20Own%20Syntax.md).


- [How Hot Reload Works](hot-reload.md)
- [Real CLR Hot Reload integration tests](../src/Akbura.HotReloadIntegrationTests/README.md)
- [Roslyn's Red-Green Trees](red-green-trees.md)
- [How x.bind is generated](expression-bindings.md)
- [Reactive AKCSS utility conditions](reactive-utility-conditions.md)
