# User Defined Hooks

> **Note:** This contract is still experimental and may change.

A use hook is a public static C# method marked with `[UseHook]`. Akbura parses its invocation as an ordinary C# expression or statement. `UseHookBinder` recognizes the attribute and records the selected constructed method in symbols, BoundTree, and operations.

## Defining A Hook

```csharp
using Akbura;
using Akbura.CompilerAnotations;
using Akbura.ComponentTree;

namespace MyProject.Hooks;

public static class QueryHooks
{
    [UseHook]
    public static State<string> useNormalizedQuery<TControl>(
        [Self] TControl control,
        string query)
        where TControl : AkburaControl =>
        new(query.Trim());
}
```

The method:

- must be `public static`;
- may have one `[Self]` parameter, which must be first and cannot be `ref`, `in`, or `out`;
- uses its exact C# method name at the call site;
- is found through the current namespace and normal or global `using` directives.

`[Self]` participates in normal generic inference and constraint checking. Both calls below select the same method:

```akbura
using MyProject.Hooks;

state string first = useNormalizedQuery(" hello ");
state string second = useNormalizedQuery(this, " world ");
```

The explicit form has normal C# overload-resolution priority. The binder only inserts `this` when the explicit call is not applicable.

## State Hooks

A state hook is the root expression of a state initializer and returns `State<T>`:

```akbura
using Akbura.Hooks;

state double width = useAvaloniaProperty(Width);
```

`useAvaloniaProperty` also demonstrates the property shorthand. Akbura first tries the ordinary C# argument. If that form is not applicable, `Width` can be replaced with `WidthProperty`:

```akbura
state double first = useAvaloniaProperty(Width);
state double second = useAvaloniaProperty(this, Width);
state double third = useAvaloniaProperty(otherControl, Control.WidthProperty);
```

The returned state observes the Avalonia property and retains the subscription for its lifetime.

## Render Hooks

A render hook returns `void` and is a standalone top-level expression statement. `useEffect` is the built-in render hook:

```akbura
using Akbura.Hooks;

state int count = 0;

useEffect(() => Console.WriteLine(count));
useEffect(() => Console.WriteLine("first render"), []);
useEffect(cancel =>
{
    Console.WriteLine(count);
    return () => Console.WriteLine("cleanup");
}, [count]);
```

Without dependencies, an effect runs after every render. With `[]`, it runs after the first render. With dependencies, it runs initially and whenever their values change. Before restarting, Akbura cancels the previous token and invokes its cleanup.

Effect callbacks support synchronous and asynchronous forms, an optional `CancellationToken`, and cleanup returned as `Action` or `IDisposable`. A custom `IUseHookDependenciesComparer` can compare dependency lists as a whole.

## Stable Hook Frames

Render hooks must be called in the same number and order on every render. Calls inside `if`, loops, nested expressions, or callbacks are rejected by semantic analysis. The runtime also verifies every completed frame and fails fast if generated or external code violates the contract.

A render that throws does not replace the last completed frame. Detaching a component cancels active effects and runs cleanup; attaching it again requests a render and restarts them.

## Current Boundary

The semantic and runtime contracts are implemented. Component code generation will consume `IUseHookSymbol`, `BoundUseHookInvocation`, and `IUseHookOperation`; it must not repeat overload resolution.
