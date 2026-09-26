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
dotnet new install Akbura.Templates::12.0.4-alpha.10
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
dotnet add package Akbura --version 12.0.4-alpha.10
:::

Then add `.UseAkbura()` to the existing `AppBuilder` chain in `Program.cs`,
before starting the application:

```csharp
using Akbura.Engine;
using Avalonia;

public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAkbura();
```

Keep the other configuration calls your application already uses.
`UseAkbura` initializes the shared Akbura engine after Avalonia's platform
services are set up. Call it once in the application builder chain.

The parameterless overload uses the default settings with no service providers.
To configure the engine, use
`UseAkbura(Action<AkburaEngineExtensions.AkburaEngineBuilder> withAkburaEngineBuilder)`.
The callback receives a builder with the following options:

| Method | Parameter and behavior |
| --- | --- |
| `WithMaxUpdatesPerBatch(int maxUpdatesPerBatch)` | Limits the consecutive update passes one component can run in a single synchronous batch, protecting against infinite update loops. Defaults to `100` (`AkburaEngine.DefaultMaxUpdatesPerBatch`). Values below `1` throw `ArgumentOutOfRangeException`. |
| `WithServiceProvider(IServiceProvider serviceProvider)` | Adds a standard .NET service provider for component `inject` declarations. |
| `WithServiceProvider(IAkburaServiceProvider serviceProvider)` | Adds a custom Akbura service provider that can use contextual injection information. |
| `WithServiceProviders<T>(T serviceProviders)` | Adds multiple standard .NET service providers, where `T` implements `IEnumerable<IServiceProvider>`. |
| `WithServiceProviders(ReadOnlySpan<IServiceProvider> serviceProviders)` | Adds a span of standard .NET service providers. |
| `WithServiceProviders(ReadOnlySpan<IAkburaServiceProvider> serviceProviders)` | Adds a span of custom Akbura service providers. |

For example, if `services` is your application's existing `IServiceProvider`,
configure it together with an update limit:

```csharp
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .UseAkbura(akbura =>
    {
        akbura
            .WithServiceProvider(services)
            .WithMaxUpdatesPerBatch(200);
    });
```

These configuration methods return the builder for chaining. `UseAkbura` calls
`Build()` automatically after the callback. Providers are queried in registration
order; standard .NET providers continue to the next provider when they return
`null`, while custom Akbura providers must explicitly forward to `NextProvider`.
See [Dependency Injection](/akbura/dependency-injection) for service registration,
lifetimes, and custom providers.

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

[Conditional Markup](/akbura/conditional-markup) describes `$if`, `$else if`,
and `$else` inside content, including branch-local C# scope and the feature's
current availability.

## Dictionary Resources

Use `x.key` to insert a child into a dictionary content slot, such as an
Avalonia control's `Resources`. `x.Key` is an alias of the same directive:

```akbura
using Avalonia.Controls;
using Avalonia.Media;

state int resourceIndex = 0;

<StackPanel>
    <StackPanel.Resources>
        <SolidColorBrush x.key="AccentBrush" Color="Red" />
        <SolidColorBrush x.Key={resourceIndex + 1} Color="Blue" />
    </StackPanel.Resources>

    <Button Click={resourceIndex++}>Move the numbered resource</Button>
</StackPanel>
```

The directive describes the entry in the parent dictionary. It does not set a
`Key` property on the brush, name the element, or replace `x.Name`.

A mutable `IDictionary<TKey, TValue>` accepts children compatible with `TValue`;
the key expression is checked against `TKey`. Non-generic `IDictionary` uses
`object` keys and values. This applies to dictionary properties, `[Content]`
properties, dictionaries used as element content, and dictionary component
parameters. Explicit interface implementations are supported.

A dictionary parameter named `Content` has a per-component mutable backing
dictionary. Other dictionary parameters retain the usual parameter rules:
provide their dictionary through an attribute or declare a default initializer,
for example `param IDictionary<int, SolidColorBrush> Entries = new
Dictionary<int, SolidColorBrush>();`. Entries inside `<MyComponent.Entries>`
populate that dictionary; they do not assign the parameter itself or satisfy a
missing required receiver.

Quoted keys remain strings, including their spaces and case. For an int-keyed
dictionary, use `x.key={42}`; `x.key="42"` is a type error. In an object-keyed
dictionary these produce different keys. No implicit `ToString()` conversion is
performed. Missing keys, keys outside a dictionary, incompatible types, and
`x.key="A" x.Key="B"` on the same child produce semantic diagnostics.

### Updating owned entries

Akbura evaluates each key expression once for the corresponding update and
reconciles the declaration's whole set of entries. Changing `resourceIndex`
moves the compatible existing brush to its new key and removes its old entry.
Two owned entries can exchange keys without a temporary collision caused by
adding one before removing the other.

Ownership belongs to a content slot, not to the dictionary as a whole. Akbura
does not call `Resources.Clear()` or overwrite foreign entries. An entry is
removed only while its key still refers to the owned object; externally replaced
values are left alone. Structural Hot Reload also removes owned entries when
their declaration or property element disappears.

Dictionary lookups and insertion use the actual dictionary's equality rules.
A case-insensitive dictionary may therefore reject `"A"` and `"a"` as a
collision, even if the compiler cannot prove it statically. A conflicting foreign
entry or duplicate desired key fails the update rather than being overwritten.
Read-only dictionaries, non-generic fixed-size dictionaries, and ambiguous
mutable dictionary contracts are unsupported write targets.

If a custom dictionary mutator throws, reconciliation attempts to restore the
previous owned entries. This is best-effort rollback: arbitrary dictionaries may
publish notifications during removal, insertion, or restoration. Notification
atomicity is not guaranteed; if restoration also fails, both failures are
reported. A new ownership snapshot is committed only after successful
reconciliation.

## Avalonia Styles

Native Avalonia `Style` and `Setter` objects can be declared directly in markup:

```akbura
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

state bool highlighted = false;

<StackPanel>
    <StackPanel.Styles>
        <Style Selector="Button">
            <Setter
                Property="Background"
                Value={highlighted ? Brushes.Red : Brushes.Blue} />

            <Style Selector="^:pointerover">
                <Setter Property="Opacity" Value="0.7" />
            </Style>
        </Style>
    </StackPanel.Styles>

    <Button Click={highlighted = !highlighted}>Change the style</Button>
</StackPanel>
```

Each child is added through its applicable typed `Add` overload: setters and
nested styles are different content routes. This fallback is also available to
custom types with suitable accessible instance `Add` methods; ambiguous
overloads produce a diagnostic.

Style subtrees are fully initialized before being attached to a live host.
Currently, each applicable component render recreates and replaces the owned
style subtree, even when its values are unchanged. Hot Reload also creates a new
subtree. The surrounding live control tree and foreign styles are preserved;
this does not rely on changing an already attached `Setter.Value` in place.

### Avalonia property references

For a destination whose declared type is `AvaloniaProperty` or one of its derived
types, a quoted value is resolved to a compatible static property field:

```akbura
<Setter Property="Button.Background" Value="Red" />
```

This refers to `Button.BackgroundProperty`, including properties inherited from
a base class. Styled, attached, and direct property fields are supported. The
same mechanism applies to custom CLR properties of type `AvaloniaProperty`,
regardless of the holder class or property name.

Inside a known style target, the owner can be omitted:

```akbura
<Style Selector="Button /template/ Border">
    <Setter Property="Background" Value="Red" />
</Style>
```

Here `Background` is resolved for the selected `Border`, not the first `Button`
in the selector. The referenced property's value type supplies the contextual
conversion for `Value`; its actual CLR type remains `object`.

The supported literal selector forms include type names, namespace-qualified
type names with `|`, classes, names, simple pseudo-classes, `:is(Type)`, `^`,
descendant and child combinators, `/template/`, and comma-separated lists.
Nested `^` inherits the parent target. `ControlTheme.TargetType` also supplies
target context. Advanced selector expressions such as `:not(...)`, nth-child
functions, and property filters are not currently supported as markup literals.

For type-less, dynamic, or ambiguous targets, use an owner-qualified reference
or a statically resolvable C# field:

```akbura
<Setter Property="Button.Background" Value="Red" />
<Setter Property={Button.BackgroundProperty} Value="Red" />
```

Akbura does not guess a target from the nearest visual parent. An unqualified
reference in a selector list must resolve consistently for all target branches.

### Property metadata and custom assignments

`[DependsOn]`, `[AssignBinding]`, and `[Content]` have independent roles:

- `[DependsOn]` orders assignment actions. It does not create a subscription,
  watcher, effect, or hook.
- `[AssignBinding]` stores the binding object in the holder property instead of
  applying the binding to that property.
- `[Content]` chooses the destination for implicit element content.

For example, a custom holder can use different names from Avalonia's `Setter`:

```csharp
using Avalonia;
using Avalonia.Data;
using Avalonia.Metadata;

namespace MyApp.Markup;

public sealed class CustomAssignment
{
    public AvaloniaProperty? Target { get; set; }

    [Content]
    [AssignBinding]
    [DependsOn(nameof(Target))]
    public object? Payload { get; set; }
}
```

For an `object` property depending on one unambiguous Avalonia property
reference, Akbura uses that reference's value type for ordinary value conversion.
It does not change the declared `object` type or apply this convention to
unrelated dependencies.

These three content routes share the assignment contract:

```akbura
using Avalonia.Controls;
using MyApp.Markup;

<CustomAssignment Payload="Red" Target="Button.Background" />

<CustomAssignment Target="Button.Background">
    Red
</CustomAssignment>

<CustomAssignment Target="Button.Background">
    <CustomAssignment.Payload>Red</CustomAssignment.Payload>
</CustomAssignment>
```

`Target` is assigned before `Payload`, including when the dependency is declared
through a property element. Independent assignments retain source order.
Invalid dependency metadata and cycles are diagnosed. A dependency not assigned
in the declaration does not create an artificial assignment.

If a dynamic non-generic `AvaloniaProperty` does not reveal its value type, use
a qualified/static reference or an explicitly typed value expression for
contextual conversion. Conflicting property dependencies are diagnosed rather
than choosing an arbitrary one.

With `[AssignBinding]`, this stores the extension result as a binding object:

```akbura
using Avalonia.Controls;
using Akbura.Markup;
using MyApp.Markup;

<CustomAssignment
    Target="Button.Background"
    Payload=${Binding AccentBrush} />
```

`ProvideValue` is still evaluated with the normal service provider. An extension
declared to return `object` follows the same delivery policy when its actual
result is a binding. The holder's own runtime contract decides how that object
is later used. See [Markup Extensions](/akcss/markup-extensions) for extension
evaluation and [AXAML syntax differences](/akbura/xml-xaml-axaml#dictionary-keys)
for the directive spelling.

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

Read [Reactive State](/akbura/state) for initialization, directional paths,
observable sources, null handling, cleanup, Hot Reload, and editor behavior.

## Parameters

Parameters define a component's public API:

```akbura
param int UserId = 1;
param string Title;
param bind string Search = "";
param out TaskItem SelectedTask;
```

Parameters without default values are required. A default parameter accepts
normal parent-to-child assignment, `bind` allows normal, two-way, and output
forms, and `out` accepts only output binding from the child to the parent. Read
[Parameters and directional bindings](/akbura/parameters) for the full contract
and parent/child examples.

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

See [Composable Hooks](/akbura/hooks) for derived state, refs, timers, throttle,
async loading, external subscriptions and reducers built from these primitives.

## Commands

Commands let a child declare a typed operation while its parent supplies a lambda, delegate, method group, or compatible command. The child can adapt a callable directly to an ordinary Avalonia `ICommand` property:

```akbura
// NavButton.akbura
command void NavigateTo(NavButton button);

<Button Command={async () => await NavigateTo.Execute(this)}>
    Navigate
</Button>
```

The parent supplies the contract with `<NavButton NavigateTo={button => button.IsActive = true} />`. A ready command can instead use `Command={NavigateTo} CommandParameter={this}` and keeps its identity.

Command facades provide awaitable `Execute` plus observable `CanExecute` and `IsExecuting` state. Ordinary `ICommand` adapters support zero- or one-parameter synchronous, `Task`, and `ValueTask` handlers. See [Commands](/akbura/commands) for parameter validation, discarded `ICommand` results, execution state, IDE support, and direct ready-command assignment.

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
