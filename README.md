[![Discord](https://img.shields.io/discord/1442893504085757984?color=8a2be2&label=discord)](https://discord.gg/zMj4MmJ9U5)
[![Telegram](https://raw.githubusercontent.com/Patrolavia/telegram-badge/master/chat.svg)](https://t.me/akburaui)

# Akbura

Akbura is an experimental .NET UI language and compiler built around declarative components, reactive state, Avalonia controls, and a typed styling language called AKCSS.

Akbura is still early. Syntax, semantic rules, code generation, and runtime APIs may change while the compiler is being shaped.

## Contents

* [Quick Start](#quick-start)
* [Files And Component Names](#files-and-component-names)
* [Top-Level Syntax](#top-level-syntax)
* [Markup](#markup)
* [State](#state)
* [Parameters](#parameters)
* [Dependency Injection](#dependency-injection)
* [Commands](#commands)
* [useEffect](#useeffect)
* [Conditional Rendering](#conditional-rendering)
* [User Hooks](#user-hooks)
* [AKCSS](#akcss)
* [AKCSS Utilities](#akcss-utilities)
* [AKCSS Imports And Lookup](#akcss-imports-and-lookup)
* [Current Limits](#current-limits)

## Quick Start

Install the package:

```bash
dotnet add package Akbura
```

Create `Counter.akbura`:

```akbura
using Avalonia.Controls;

namespace Demo.Pages;

state int count = 0;

<StackPanel>
    <TextBlock Text={$"Count: {count}"}/>
    <Button Click={count++}>Increment</Button>
</StackPanel>
```

Run it from a host application:

```csharp
using Akbura;

AkburaRoot.Run<Demo.Pages.Counter>();
```

Avalonia applications can also host Akbura components through the Avalonia host integration:

```xml
<akbura:AkburaAvaloniaHost Component="{x:Type local:Counter}" />
```

## Files And Component Names

An `.akbura` file declares one component. By default, the component name is the file name.

```text
Pages/Counter.akbura -> Counter
```

The default namespace is derived from the project root namespace plus the file path. You can override it with a normal namespace declaration:

```akbura
namespace Demo.Pages;

<TextBlock Text="Hello"/>
```

C# partial classes can extend the generated component type:

```csharp
namespace Demo.Pages;

public partial class Counter
{
    public void Reset()
    {
    }
}
```

## Top-Level Syntax

An Akbura component can contain these top-level members:

```akbura
using System;
using Avalonia.Controls;
using Demo.Styles.Shared.akcss;

namespace Demo.Pages;

@akcss {
    .card { Padding: (10, 20); }
}

inject ILogger<DashboardPage> logger;

param int UserId = 1;
param bind string Search = "";
param out TaskItem SelectedTask;

state bool isOpen = false;
state DashboardVm vm = new DashboardVm();
state bool isBusy = bind vm.IsBusy;
state TaskItem activeTask = in vm.ActiveTask;
state TaskItem selectedTask = out vm.SelectedTask;

command int Refresh(int userId);

useEffect(UserId, isBusy, Refresh.IsExecuting) {
    logger.LogInformation("Refreshing {0}", UserId);
}

if(isOpen)
{
    Console.WriteLine("Panel opened");
    <TextBlock Text="Opened"/>
}

<StackPanel class="card">
    <TextBlock Text="Dashboard"/>
</StackPanel>
```

Top-level C# statements are allowed. Akbura symbols such as `state`, `inject`, and `command` are made visible to C# semantic analysis where supported.

## Markup

Markup uses Avalonia-style components and Akbura attributes:

```akbura
using Avalonia.Controls;

state string title = "Dashboard";
state bool isOpen = false;

<StackPanel class="card" w-30 {isOpen}:opacity-50>
    <TextBlock Text={title}/>
    <Button Click={isOpen = true}>Open</Button>
    <Border IsVisible={isOpen}/>
</StackPanel>
```

Supported component names include:

```akbura
<Button/>
<Avalonia.Controls.Button/>
<global::Avalonia.Controls.Button/>
<alias::Avalonia.Controls.Button/>
<List{TaskItem}/>
```

Plain attributes set properties:

```akbura
<TextBlock Text="Hello"/>
<TextBlock Text={title}/>
```

Directional attributes express data flow:

```akbura
<TextBox bind:Text={title}/>
<TaskList out:Selected={selectedTask}/>
```

Routed events bind to expressions or delegates:

```akbura
state int count = 0;

<Button Click={count++}/>
<Button Click={(sender, args) => count++}/>
<Button Click={(_, args) => {
    if(count == 5) {
        Console.WriteLine("Hello");
    }

    count++;
}}/>
```

`bind:` and `out:` are not valid for routed events.

## State

State is declared with `state`:

```akbura
state count = 0;
state int selectedIndex = 0;
state string query = "";
state ReactList tasks = bind viewModel.Tasks;
```

State can bind to view-model members:

```akbura
state MyViewModel vm = new MyViewModel();

state string name = bind vm.Name;
state string fullName = out vm.FullName;
state string surname = in vm.Surname;
```

Binding modes:

| Syntax | Meaning |
| --- | --- |
| `state T x = value;` | Local component state. |
| `state T x = bind vm.Name;` | Two-way binding. |
| `state T x = out vm.Name;` | Read from source, component state is readonly. |
| `state T x = in vm.Name;` | Write to target. |

`in`, `out`, and `bind` state initializers must use a binding path:

```akbura
state string ok1 = in vm.Name;
state string ok2 = in vm.People[0].Name;
state int bad = in vm.Age + 1; // invalid binding expression
```

For `bind` and `out`, Akbura warns when the source cannot be observed as `IObservable<T>` and the owner does not implement `INotifyPropertyChanged`. For `in` and writable parts of `bind`, the target must be a writable property or field.

## Parameters

Parameters are component inputs and outputs:

```akbura
param int UserId = 1;
param string Title;
param bind string Search = "";
param out TaskItem SelectedTask;
```

Parameter modes:

| Declaration | Direction | Description |
| --- | --- | --- |
| `param T x` | Parent to component | Default. Parent sets the value. |
| `param bind T x` | Parent to component and component to parent | Two-way binding. |
| `param out T x` | Component to parent | Component pushes values upward. |

There is no `param in T x`; default `param T x` already behaves as input.

Example child component:

```akbura
// TaskCard.akbura
param TaskItem Item;
param bind bool IsSelected = false;
param out TaskItem SelectedItem;

<Button Click={IsSelected = true}>
    {Item.Title}
</Button>
```

Example parent:

```akbura
state TaskItem selectedTask = null!;
state bool selected = false;

<TaskCard Item={task} bind:IsSelected={selected} out:SelectedItem={selectedTask}/>
```

Duplicate write setters are rejected:

```akbura
<TextBlock bind:Text={name} Text={other}/> // invalid
<TextBlock Text={name} Text={other}/>      // invalid
```

Read-only output bindings can coexist with a setter:

```akbura
<TextBlock Text={name} out:Text={other}/>
<TextBlock bind:Text={name} out:Text={other}/>
<TextBlock out:Text={a} out:Text={b}/>
```

## Dependency Injection

Injected services are declared with `inject`:

```akbura
inject ILogger<Counter> logger;

state int count = 0;

logger.LogInformation("Count is {0}", count);

<Button Click={count++}>Increment</Button>
```

The component file name is also available as the component type, so `ILogger<Counter>` works inside `Counter.akbura`.

## Commands

Commands expose interaction points from a component:

```akbura
// CustomButton.akbura
command int Click(int value);

state int clicked = 0;

useEffect(Click.IsExecuting) {
    Console.WriteLine("Command is executing");
}

<Button Click={async () => {
    var result = await Click.Execute(clicked++);
    Console.WriteLine($"Result is {result}");
}}>
    Run
</Button>
```

A command facade provides:

| Member | Meaning |
| --- | --- |
| `Execute(args)` | Executes the command. The facade is asynchronous and returns `ValueTask<T>` for result commands. |
| `CanExecute` | Reactive execution permission state. |
| `IsExecuting` | Reactive execution state. |

Command declarations use the result type directly:

```akbura
command int Refresh(int userId);
command void Save();
```

Do not declare the command as `Task<int>` just to make it asynchronous. The generated command facade provides the `ValueTask` execution shape.

Parent components can bind to child commands:

```akbura
<CustomButton Click={() => Console.WriteLine("Hello")}/>
<CustomButton Click={x => Console.WriteLine($"Clicked {x}")}/>
<CustomButton Click={x => x * 2}/>
<CustomButton Click={async x => await viewModel.Fetch(x)}/>
<CustomButton Click={viewModel.MyCommand}/>
```

## useEffect

`useEffect` declares effect code that reacts to dependencies:

```akbura
param int UserId = 1;
state int count = 0;
inject IDataService service;
command void Refresh(int userId);

useEffect(UserId, count, service.Value, Refresh.IsExecuting) {
    Console.WriteLine(count);
}
cancel {
    Console.WriteLine("cancel");
}
finally {
    Console.WriteLine("done");
}
```

`useEffect` dependencies can reference parameters, state, injected members, and command facade state.

## Conditional Rendering

Akbura supports normal top-level C# control flow. A top-level `if` block can contain C# statements and markup siblings:

```akbura
state bool isOpen = false;

if(isOpen)
{
    Console.WriteLine("Opened");
    <TextBlock Text="Opened"/>
}

<Button Click={isOpen = true}>Open</Button>
```

Razor-style markup control flow is intentionally not supported:

```akbura
<Button>
    @if(isOpen) {
        <TextBlock Text="Opened"/>
    }
</Button>
```

For predictable UI today, prefer classic visibility:

```akbura
state bool isBoxVisible = true;

<StackPanel>
    <Border class="box" IsVisible={isBoxVisible}/>
    <Border class="box" {!isBoxVisible}:hidden/>
</StackPanel>
```

## User Hooks

User hooks are experimental. Hook calls are allowed only from top-level state initializers:

```akbura
state string query = "";
state string name = useName(query);
```

Hooks cannot be called from conditional or loop bodies:

```akbura
if(query.Length > 0)
{
    useName(query); // invalid
}
```

The C# hook type is expected to be annotated and to provide a `UseHook` method:

```csharp
[UserHook]
public struct UseNameHook
{
    public string UseHook<T>(AkburaComponent component, StateInfo<T> state)
    {
        return state.FieldName ?? "Unnamed State";
    }
}
```

## AKCSS

AKCSS is Akbura's typed styling language. It can be written inline in an `.akbura` file or in `.akcss` files.

Inline AKCSS:

```akbura
@akcss {
    .card {
        Padding: (10, 20);
        Background: White;
    }
}

<Border class="card"/>
```

Standalone AKCSS:

```akcss
// DashboardPage.akcss
.card {
    Padding: (10, 20);
    Background: White;
}
```

Selectors:

```akcss
.anyControl {
    Opacity: 1;
}

Button.primary {
    Background: White;
}

(global::Demo.Components.TaskCard) {
    Padding: 5;
}

(global::Demo.Components.TaskCard).selected {
    Padding: 10;
}
```

AKCSS supports namespace imports:

```akcss
@using Demo.Components;

TaskCard.selected {
    Padding: 10;
}
```

Property values are semantically bound against the target property type:

```akcss
Button.primary {
    Background: White;
    Background: "#FFAA";
    Foreground: Color.FromRgb(33, 11, 231);

    FontWeight: Bold;
    FontWeight: FontWeight.Bold;
    FontWeight: (FontWeight)700;

    HorizontalAlignment: Center;
    VerticalAlignment: (VerticalAlignment)2;

    Padding: (10, 20);
    Padding: (10, 20, 30, 40);
    Margin: (top: 10, bottom: 30);
    Margin: (vertical: 5);

    Width: Amx.DynamicResource<double>("--dashboard-width");
}
```

For enum-like properties, a bare value such as `Bold`, `Center`, or `Compact` is resolved from the expected property type.

AKCSS body directives:

```akcss
.card {
    Background: White;

    @if(IsHovered) {
        Background: "#FFAA";
    }

    @apply surface shadow;
}
```

`@intercept` hands a style to C#:

```akcss
.veryComplex {
    @intercept global::Demo.Styles.DashboardStyle;
}
```

The C# type must inherit from `Akbura.Akcss.AkcssClass`:

```csharp
public sealed class DashboardStyle : Akbura.Akcss.AkcssClass
{
    public override void Update(object control)
    {
    }
}
```

If a style contains `@intercept`, other AKCSS body members in that same style are ignored and reported as warnings.

## AKCSS Utilities

Utilities are defined inside `@utilities`:

```akcss
@utilities {
    .w-(double value) {
        Width: value;
    }

    .px-(double width) {
        Padding: (horizontal: Amx.DynamicResource<double>("--spacing") * width);
    }

    .hidden {
        IsVisible: false;
    }

    Button.primary {
        Background: White;
    }
}
```

Use utilities in markup:

```akbura
state bool isBusy = false;

<StackPanel w-30 px-{12} {isBusy}:hidden/>
```

Utility arguments come from numeric segments and expression segments:

```akbura
<Border w-30/>
<Border w-{width}/>
<Border px-{viewModel.Spacing}/>
```

Conditional prefixes are supported:

```akbura
<Border {isBusy}:hidden/>
<Border sm:w-20/>
```

Utility intercept types inherit from `Akbura.Akcss.AkcssUtility`:

```akcss
@utilities {
    .w-(double value) {
        @intercept global::Demo.Styles.WidthUtility;
    }
}
```

## AKCSS Imports And Lookup

AKCSS lookup uses layers:

1. Inline `@akcss` in the component.
2. Companion AKCSS file next to the component, for example `Button.akbura` and `Button.akcss`.
3. Explicit AKCSS imports in `using ...akcss;` declarations.

Example component import:

```akbura
using Demo.Styles.Shared.akcss;

<Button primary w-30/>
```

Example AKCSS import:

```akcss
@using Demo.Styles.Shared.akcss;

.primary {
    @apply sharedStyle w-30;
}
```

Local AKCSS utilities and styles win over imported ones. Duplicate matches in the same layer are reported as ambiguity diagnostics.

## Current Limits

Akbura is still evolving. The current compiler supports a growing syntax and semantic model, but some runtime/codegen behavior is still being built.

Important current limits:

* Markup-internal `@if`, `@else`, `@for`, and `@foreach` are not supported.
* Top-level conditional rendering parses and has symbols, while full branch rendering code generation is still evolving.
* AKCSS `@intercept` disables other body members in the same style.
* AKCSS code generation and runtime optimization are still being refined.
* User hooks are experimental and must stay in top-level state initializers.
