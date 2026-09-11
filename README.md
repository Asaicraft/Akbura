# Akbura

[![NuGet](https://img.shields.io/nuget/vpre/Akbura?logo=nuget&label=NuGet)](https://www.nuget.org/packages/Akbura)
[![Discord](https://img.shields.io/discord/1442893504085757984?color=8a2be2&label=discord)](https://discord.gg/zMj4MmJ9U5)
[![Telegram](https://raw.githubusercontent.com/Patrolavia/telegram-badge/master/chat.svg)](https://t.me/akburaui)
[![Documentation](https://img.shields.io/badge/documentation-Akbura-0ea5e9)](https://asaicraft.github.io/Akbura/)
[![Gallery](https://img.shields.io/badge/gallery-Akbura-22c55e)](https://asaicraft.github.io/Akbura/Gallery/)

Akbura is an experimental declarative UI language and compiler for .NET and Avalonia, with reactive state and typed styling through AKCSS.

The `Akbura` package includes BlackSilence, the production incremental compiler. Furioso remains only as a compatibility baseline for tests and benchmarks.

> [!WARNING]
> Akbura is under active development. Syntax and APIs may change.

## Getting started

Install the current Akbura templates from NuGet:

```bash
dotnet new install Akbura.Templates::12.0.4-alpha.4
```

Create and run an Avalonia desktop application:

```bash
dotnet new akbura.app -n MyApp
cd MyApp
dotnet run
```

The template includes Akbura, AKCSS, Debug diagnostics, and optional dependency
injection. Use `--di Microsoft.Extensions.DependencyInjection` or
`--di Splat.Locator` when creating the project to select a DI provider.

To add Akbura to an existing Avalonia project instead, install the package and
create a component with the item template:

```bash
dotnet add package Akbura --version 12.0.4-alpha.4
dotnet new akbura.component -n Counter --namespace MyApp.Components -o Components
```

For a component with a C# code-behind partial class, use
`dotnet new akbura.partial-component` instead.

Install the editor extension for the IDE you use:

- **VS Code:** install [Akbura Vs Code Extension](https://marketplace.visualstudio.com/items?itemName=asaicraft.akbura-language-server), or run `code --install-extension asaicraft.akbura-language-server`.
- **Visual Studio:** install [Akbura Visual Studio Extension](https://marketplace.visualstudio.com/items?itemName=asaicraft.akbura-visual-studio-extension) from the Marketplace or search for its name under **Extensions → Manage Extensions**.

The extensions provide language support for `.akbura` and `.akcss` files. The
NuGet package remains responsible for compiling those files as part of the
project build.

## First component

```csharp
using Avalonia.Controls;

namespace MyApp.Components;

inject ILogger<Counter> Logger;

state int count = 0;

useEffect(() =>
{
    Logger.LogInformation("Button width is {Width}", button.Width);
}, [count]);

<Button Click={() => count++} x.Name="button" w-10 h-4 p-3>
    Count is {count}
</Button>
```

For installation, language syntax, state, commands, hooks, and AKCSS, see the **[Akbura documentation](https://asaicraft.github.io/Akbura/)**.

## Links

- [Akbura on NuGet](https://www.nuget.org/packages/Akbura)
- [Akbura Templates on NuGet](https://www.nuget.org/packages/Akbura.Templates)
- [Akbura Vs Code Extension](https://marketplace.visualstudio.com/items?itemName=asaicraft.akbura-language-server)
- [Akbura Visual Studio Extension](https://marketplace.visualstudio.com/items?itemName=asaicraft.akbura-visual-studio-extension)
- [Akbura documentation](https://asaicraft.github.io/Akbura/)
- [Discord](https://discord.gg/zMj4MmJ9U5)
- [Telegram](https://t.me/akburaui)
- [License](https://github.com/Asaicraft/Akbura/blob/master/LICENSE.txt)
