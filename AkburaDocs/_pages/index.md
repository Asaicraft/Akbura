---
title: Akbura Documentation
summary: Learn how to build declarative, reactive Avalonia interfaces with Akbura.
---

## Quick Start

::: warning Experimental
Akbura is under active development. Syntax, semantic rules, generated code, and runtime APIs may change between releases.
:::

Install the package in your .NET project:

:::sh
dotnet add package Akbura
:::

Create a component named `Counter.akbura`:

```akbura
using Avalonia.Controls;

namespace Demo.Pages;

state int count = 0;

<StackPanel Spacing="12">
    <TextBlock Text={$"Count: {count}"}/>
    <Button Click={count++}>Increment</Button>
</StackPanel>
```

Run the component from a host application:

```csharp
using Akbura;

AkburaRoot.Run<Demo.Pages.Counter>();
```

An existing Avalonia application can host the same component directly:

```xml
<akbura:AkburaAvaloniaHost Component="{x:Type local:Counter}" />
```

## How Akbura Works

Akbura combines a dedicated UI language with normal C# and Avalonia APIs:

1. You describe a component in an `.akbura` file.
2. The compiler parses Akbura markup together with embedded C#.
3. Semantic analysis resolves controls, properties, events, bindings, hooks, and AKCSS.
4. Akbura generates a typed C# component that renders native Avalonia controls.

An `.akbura` file declares one component. Its default component name comes from the file name:

```text
Pages/Counter.akbura -> Demo.Pages.Counter
```

Generated components are partial, so regular C# files can extend them when needed.

## Core Concepts

### Components and Markup

Markup uses Avalonia controls directly. Plain attributes set properties, expressions read C# values, and routed events accept expressions or delegates:

```akbura
state string title = "Dashboard";
state bool isOpen = false;

<StackPanel>
    <TextBlock Text={title}/>
    <Button Click={isOpen = true}>Open</Button>
    <Border IsVisible={isOpen}/>
</StackPanel>
```

Component names can be short, fully qualified, global, aliased, or generic.

### Reactive State

Declare local state with `state` and use it directly from markup:

```akbura
state int selectedIndex = 0;
state string query = "";
```

State can also connect to a view model through `bind`, `in`, and `out` binding modes:

```akbura
state MyViewModel vm = new MyViewModel();

state string name = bind vm.Name;
state string fullName = out vm.FullName;
state string surname = in vm.Surname;
```

### Parameters and Data Flow

Parameters define a component's public inputs and outputs:

```akbura
param int UserId = 1;
param string Title;
param bind string Search = "";
param out TaskItem SelectedTask;
```

Use `bind:` for two-way component property flow and `out:` when a child publishes a value to its parent.

### Effects and Hooks

`useEffect` runs work after rendering and can react to a dependency collection:

```akbura
using Akbura.Hooks;

state int count = 0;

useEffect(
    () => Console.WriteLine(count),
    [count]);
```

Akbura also supports `useAvaloniaProperty` for observing Avalonia properties and experimental user-defined hooks.

### Commands

Commands expose typed interaction points with reactive execution state:

```akbura
command int Refresh(int userId);

<Button Click={async () => {
    var result = await Refresh.Execute(42);
    Console.WriteLine(result);
}}>
    Refresh
</Button>
```

Command facades provide `Execute`, `CanExecute`, and `IsExecuting`.

### AKCSS

AKCSS is Akbura's typed styling language. Styles can live inline or in standalone `.akcss` files:

```akbura
@akcss {
    .card {
        Padding: (10, 20);
        Background: White;
    }
}

<Border class="card"/>
```

Property values are bound against the actual Avalonia property type. AKCSS also supports reusable utilities, `@apply`, conditional rules, imports, dynamic resources, and C# interceptors.

## Project Status

Akbura is currently experimental and is not yet a stable production framework. The compiler already supports a growing syntax and semantic model, while parts of rendering, code generation, tooling, and runtime optimization are still evolving.

Current limitations include:

- Markup-internal `@if`, `@else`, `@for`, and `@foreach` are not supported.
- Full code generation for top-level conditional branches is still evolving.
- User hooks are experimental and must keep a stable top-level call order.
- AKCSS code generation and runtime optimization are still being refined.

Use the project today to experiment, follow development, report issues, and help shape the language.

## Community and Feedback

- [GitHub repository](https://github.com/Asaicraft/Akbura) — source code, issues, and development history.
- [Discord](https://discord.gg/zMj4MmJ9U5) — discuss the compiler and share feedback.
- [Telegram](https://t.me/akburaui) — follow updates and talk with the community.

Ideas, bug reports, documentation improvements, and focused pull requests are welcome.