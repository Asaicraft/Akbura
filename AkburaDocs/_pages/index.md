---
title: Akbura Documentation
summary: Learn how to build declarative, reactive Avalonia interfaces with Akbura.
---

## Quick Start

::: warning Experimental
Akbura is under active development. Syntax, generated code, and runtime APIs may change between releases.
:::

Install the package:

:::sh
dotnet add package Akbura
:::

Create `Counter.akbura`:

```akbura
using Avalonia.Controls;

namespace Demo.Pages;

state int count = 0;

<StackPanel Spacing="12">
    <TextBlock Text={$"Count: {count}"}/>
    <Button Click={count++}>Increment</Button>
</StackPanel>
```

Use the generated component directly inside an Avalonia AXAML view:

```xml
<UserControl
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:pages="using:Demo.Pages">

    <pages:Counter />

</UserControl>
```

Akbura components are Avalonia controls, so no separate host control is required.

## What Akbura Is

Akbura is a library and compiler for Avalonia, not a replacement framework.

It adds a declarative component language, reactive state, typed expressions, hooks, commands, and AKCSS while continuing to use native Avalonia controls and APIs.

An `.akbura` file declares one component:

```text
Pages/Counter.akbura -> Demo.Pages.Counter
```

Generated components are partial and can be extended with regular C#.

## Components and Markup

Akbura markup uses Avalonia controls directly:

```akbura
state string title = "Dashboard";
state bool isOpen = false;

<StackPanel>
    <TextBlock Text={title}/>
    <Button Click={isOpen = true}>Open</Button>
    <Border IsVisible={isOpen}/>
</StackPanel>
```

Attributes may contain literals, C# expressions, bindings, and markup extensions.

## Reactive State

Declare local reactive values with `state`:

```akbura
state int selectedIndex = 0;
state string query = "";
```

State may also connect to object properties:

```akbura
state MyViewModel vm = new MyViewModel();

state string name = bind vm.Name;
state string fullName = out vm.FullName;
state string surname = in vm.Surname;
```

## Parameters

Parameters define a component's public API:

```akbura
param int UserId = 1;
param string Title;
param bind string Search = "";
param out TaskItem SelectedTask;
```

Parameters without default values are required. `bind` enables two-way flow, while `out` publishes a value to the parent.

## Binding

Akbura supports Avalonia bindings directly in markup:

```akbura
<TextBlock Text=${Binding Title} />
<TextBox Text=${Binding Search, Mode=TwoWay} />
```

Bindings are resolved against the expected property type and may be compiled when a data type is known.

Inside item templates, Akbura can infer the item type from `ItemsSource`. A template must contain a single root control, so multiple child controls should be wrapped in a panel:

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate x.ItemName="item">
        <StackPanel Spacing="6">
            <TextBlock Text=${Binding Title} />

            <Button Click={() => Open(item)}>
                Open {item.Id.ToString("D")} — {item.Title}
            </Button>
        </StackPanel>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

`x.ItemName` exposes the current item as a typed variable. The item can participate in property expressions, event handlers, method calls, and inline content expressions.

Use `x.DataType` when the item type cannot be inferred automatically.

## Markup Extensions

Akbura does not restrict markup attributes to a special binding-only syntax. Regular markup extensions can be used directly alongside C# expressions and literals:

```akbura
<TextBlock Text=${Binding Title} />
<Border Background=${StaticResource CardBackground} />
<Border BorderBrush=${DynamicResource AccentBrush} />
```

Custom markup extensions are also supported when they expose a compatible constructor and `ProvideValue` method:

```akbura
<TextBlock Text=${Format 1, Value={count}} />
```

The compiler resolves the extension type, constructor arguments, properties, `ProvideValue`, and the conversion to the target Avalonia property type.

## Effects and Hooks

`useEffect` runs after rendering and can react to dependencies:

```akbura
using Akbura.Hooks;

state int count = 0;

useEffect(
    () => Console.WriteLine(count),
    [count]);
```

Akbura also supports Avalonia-property hooks and experimental user-defined hooks.

## Commands

Commands expose typed operations with reactive execution state:

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

## AKCSS

AKCSS is Akbura's typed styling language:

```akbura
@akcss {
    .card {
        Padding: (10, 20);
        Background: White;
    }
}

<Border class="card"/>
```

AKCSS supports reusable classes, utilities, `@apply`, conditional rules, resources, imports, and C# interceptors.

## Project Status

Akbura is experimental and is not yet intended as a stable production library.

Current limitations include:

- markup-level `@if`, `@else`, `@for`, and `@foreach` are not supported;
- some APIs and generated code may change without backward compatibility.

## Community and Feedback

- [GitHub repository](https://github.com/Asaicraft/Akbura)
- [Discord](https://discord.gg/zMj4MmJ9U5)
- [Telegram](https://t.me/akburaui)

Ideas, bug reports, documentation improvements, and focused pull requests are welcome.
