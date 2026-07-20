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

Avalonia property changes do not request a component render. Use the render overload of `useAvaloniaProperty` when an effect must react directly to them:

```akbura
useAvaloniaProperty(cancel =>
{
    Console.WriteLine($"Size: {Width} x {Height}");
    return () => Console.WriteLine("cleanup");
}, [Width, Height]);
```

It participates in the same stable hook frame as `useEffect`, subscribes to each `AvaloniaProperty`, and performs cancellation and cleanup before restarting the callback.

## Custom Runtime State

A third-party render hook can own persistent state through the same primitive used by the built-in hooks. Adding one does not require a new method or a new slot type in `AkburaControl`:

```csharp
public static class CounterHooks
{
    private static readonly UseHookKey s_key = new();

    [UseHook]
    public static void useRenderCount(
        [Self] AkburaControl control,
        Action<int> rendered)
    {
        control.UseHook(
            s_key,
            rendered,
            static _ => new CounterState(),
            static (state, callback) => callback(++state.Count),
            static state => state.Detach());
    }

    private sealed class CounterState
    {
        public int Count { get; set; }

        public void Detach()
        {
        }
    }
}
```

`UseHookKey` uses reference identity. Keep one static key for each logical hook contract;
overloads that share the same runtime state and behavior may share that key.

`createState` runs once for that call position. `apply` receives the latest frame arguments after every completed render. The optional `detach` callback releases subscriptions or other resources. On the next attached frame the same state is applied again.

All calls registered through `UseHook` share one frame counter. Changing the number, order, key, or runtime state type causes `AkburaUseHooksFrameChangedException`, including for hooks defined in another assembly.

## Stable Hook Frames

Render hooks must be called in the same number and order on every render. Calls inside `if`, loops, nested expressions, or callbacks are rejected by semantic analysis. The runtime also verifies every completed frame and fails fast if generated or external code violates the contract.

A render that throws does not replace the last completed frame. Detaching a component cancels active effects and runs cleanup; attaching it again requests a render and restarts them.

## Current Boundary

The semantic and runtime contracts are implemented. Component code generation will consume `IUseHookSymbol`, `BoundUseHookInvocation`, and `IUseHookOperation`; it must not repeat overload resolution.
