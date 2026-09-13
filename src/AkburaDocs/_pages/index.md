---
title: Akbura Documentation
summary: Learn how to build declarative, reactive Avalonia interfaces with Akbura.
---

[![NuGet](https://img.shields.io/nuget/vpre/Akbura?logo=nuget&label=NuGet)](https://www.nuget.org/packages/Akbura)

| Feature | Support level | Notes |
|---------|:------------:|-------|
| Compatibility with Avalonia | Full | Akbura components can be used directly in AXAML views and vice versa – any Avalonia control (including custom controls) works without extra attributes or imports. |
| MarkupExtensions | Partial | Common markup extensions (`StaticResource`, `DynamicResource`, `Binding`) work exactly as in Avalonia. The `IServiceProvider` supplies `IProvideValueTarget`, `IRootObjectProvider`, `IUriContext`, `IXamlTypeResolver`, and `IAvaloniaXamlIlEagerParentStackProvider`. The old `{}` syntax is replaced by `${}` (no quotes). XML‑element syntax for markup extensions, `MarkupExtensionOptionAttribute`, and some advanced scenarios are not supported. |
| Binding | Full | Bindings are fully supported: both `ReflectionBinding` and `CompiledBinding` work, including `BindingPath` parsing, mode selection, converters, etc. |
| TemplateContent | Full | Properties decorated with `[TemplateContent]` are automatically handled – Akbura generates an `IDeferredContent` implementation, so templates work out of the box. |

## Getting Started

::: warning Experimental
Akbura is under active development. Syntax, generated code, and runtime APIs may change between releases.
:::

### Create an application from the template

Install the current template package from NuGet:

:::sh
dotnet new install Akbura.Templates::12.0.4-alpha.4
:::

Create and run an Avalonia desktop application:

:::sh
dotnet new akbura.app -n MyApp
cd MyApp
dotnet run
:::

The application template includes Akbura, AKCSS, Debug diagnostics, and optional
dependency injection. Select a DI provider when creating the project if needed:

:::sh
dotnet new akbura.app -n MyApp --di Microsoft.Extensions.DependencyInjection
dotnet new akbura.app -n MyApp --di Splat.Locator
:::

### Add Akbura to an existing project

Install Akbura into an existing Avalonia project:

:::sh
dotnet add package Akbura --version 12.0.4-alpha.4
:::

If the template package is installed, create a component from the project
directory:

:::sh
dotnet new akbura.component -n Counter --namespace MyApp.Components -o Components
:::

To create both `Counter.akbura` and its C# code-behind partial class
`Counter.akbura.cs`, use:

:::sh
dotnet new akbura.partial-component -n Counter --namespace MyApp.Components -o Components
:::

### Install editor support

Install the extension for the IDE you use:

- **VS Code:** install [Akbura Vs Code Extension](https://marketplace.visualstudio.com/items?itemName=asaicraft.akbura-language-server), or run `code --install-extension asaicraft.akbura-language-server`.
- **Visual Studio:** install [Akbura Visual Studio Extension](https://marketplace.visualstudio.com/items?itemName=asaicraft.akbura-visual-studio-extension) from the Marketplace or search for its name under **Extensions → Manage Extensions**.

Both extensions provide language support for `.akbura` and `.akcss` files. The
NuGet package compiles these files during the project build.

### Create your first component

Create `Counter.akbura`:

```akbura
using Avalonia.Controls;

namespace MyApp.Components;

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
    xmlns:components="using:MyApp.Components">

    <components:Counter />

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

`useEffect` without a dependency list runs after every successful render. An empty
list runs it once until the component is detached or the hook is reset. Changing
dependencies cancels the previous run and invokes its cleanup before the next run.

### Debouncing state

`useDebounce` keeps the initial value available immediately, then publishes changes
after the source has stayed unchanged for the requested delay:

```akbura
using Akbura.Hooks;

state int count = 0;
state int debouncedCount = useDebounce(count, 300);
state int debouncedEffectedCount = useDebounce(count, x => x + 1, 300);
```

Declare the source state before a hook that consumes it. For the `State<T>` argument,
the compiler passes the live state object; ordinary expressions such as `count + 1`
continue to read its value. The hook result remains the same state object across
renders, and separate calls have independent results and delays.

Both integer milliseconds and `TimeSpan` are accepted, from zero through
`Int32.MaxValue` milliseconds. A source value change, replacement of the source state,
or change to the delay restarts the full delay. Unrelated renders and updates of the
debounced result do not restart it. Changes merged into one render count as one input.
Values are captured from that render; mutable objects are not deep-copied.

The selector is called once for the initial result, then after the delay. It should
be pure: initialization may be retried if the first render fails. Each pending run
keeps the selector from the render that started it. Replacing an inline selector
alone does not restart the delay, and captured values are not automatic dependencies.

For additional dependencies, use the callback overload:

```akbura
state string query = "";
state string debouncedQuery = "";

useDebounce(
    () => { debouncedQuery = query; },
    TimeSpan.FromMilliseconds(350),
    [query]);
```

Callbacks run on the UI dispatcher, including zero-delay callbacks. The callback
form also accepts `Func<CancellationToken, Task>`; its task is awaited and failures
are observed by the effect runtime. Cancellation stops a pending delay or queued
callback. Already-running asynchronous callbacks must cooperate with the token.
Dependencies and delay select a run; an unrelated render does not replace the
callback already captured by that run.

Detaching cancels pending work and preserves hook state. Reattaching starts a full
new delay using the latest source. Hot Reload currently recreates hook-owned state;
ordinary component state follows the existing preservation rules.

### Writing a composable hook

A user-defined hook runs during each component render and combines primitives.
`useHookState` returns persistent state immediately; `useEffect` registers work to
run after a successful render. Their order must stay the same on every render.

```csharp
using Akbura;
using Akbura.CompilerAnotations;
using Akbura.ComponentTree;
using Akbura.Hooks;

public static class MyHooks
{
    [UseHook]
    public static State<int> useDoubled(
        [Self] this AkburaControl control,
        State<int> source)
    {
        var value = source.Value;
        var result = control.useHookState(() => value * 2);

        control.useEffect(
            () => { result.Value = value * 2; },
            [source, value]);

        return result;
    }
}
```

The component uses it as `state int doubled = useDoubled(count);`. Its internal
state belongs to the component and requests rendering when it changes. The author
does not manage slot numbers, attach the returned state again, or maintain a second
registry. Wrapping a hook does not allocate an additional slot: only the primitives
participate in the shared sequence.

`useHookState` accepts an initial value, a lazy `Func<T>`, or a `StateInfo<T>`
descriptor. Initialization happens only when a new slot is needed; later initial
values do not overwrite existing state. Static descriptors can be shared across
calls, while each component and primitive position has its own state instance.

Call primitives only while a render is collecting hooks. Do not call them from
conditions that change the sequence, event handlers, effect callbacks, lazy state
initializers, or after `await`. Put conditional behavior inside an effect and include
the condition in its dependencies. The runtime checks the sequence's shape and
types; it cannot distinguish every swap of otherwise identical calls in arbitrary C#.

Older hooks that create state and subscriptions once must explicitly use
`[UseHook(IsInitializer = true)]` until migrated to `useHookState` and resource-owning
effects. Akbura's existing Avalonia-property state hooks use that compatibility
contract. A normal `[UseHook]` returning `State<T>` uses the composable per-render
contract.

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

- [Akbura on NuGet](https://www.nuget.org/packages/Akbura)
- [Akbura Templates on NuGet](https://www.nuget.org/packages/Akbura.Templates)
- [Akbura Vs Code Extension](https://marketplace.visualstudio.com/items?itemName=asaicraft.akbura-language-server)
- [Akbura Visual Studio Extension](https://marketplace.visualstudio.com/items?itemName=asaicraft.akbura-visual-studio-extension)
- [GitHub repository](https://github.com/Asaicraft/Akbura)
- [Discord](https://discord.gg/zMj4MmJ9U5)
- [Telegram](https://t.me/akburaui)

Ideas, bug reports, documentation improvements, and focused pull requests are welcome.
