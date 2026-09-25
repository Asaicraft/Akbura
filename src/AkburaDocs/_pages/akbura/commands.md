# Commands

Akbura commands are typed operations declared by a child component and supplied by its parent. The generated command object exposes asynchronous execution together with observable execution state. Implementing a custom command class is not required.

## Declare and supply a command

The child declares the contract and invokes it:

```akbura
using Avalonia.Controls;

namespace Demo;

command void NavigateTo(NavButton button);

<Button Command={NavigateTo}>
    Navigate
</Button>
```

The parent supplies a handler through the native `NavigateTo` command attribute:

```akbura
using System;
using Avalonia.Controls;

namespace Demo;

<StackPanel>
    <NavButton NavigateTo={button => button.IsActive = true} />
    <NavButton NavigateTo={() => Console.WriteLine("Navigate")} />
    <NavButton NavigateTo={async button => {
        await LoadPageAsync(button);
    }} />
</StackPanel>
```

A zero-argument lambda may intentionally ignore the declared command arguments. Akbura adapts compatible synchronous and asynchronous handlers to the generated command property.

## Handler forms

Native command attributes accept compatible lambdas, method groups, typed delegate values, `Task`/`ValueTask` handlers, and compatible `IAkburaCommand` instances:

```akbura
using System;
using System.Threading.Tasks;

Func<NavButton, Task> navigateAsync = NavigateAsync;
Func<NavButton, ValueTask> navigateValueTask = NavigateValueAsync;

<StackPanel>
    <NavButton NavigateTo={NavigateAsync} />
    <NavButton NavigateTo={navigateAsync} />
    <NavButton NavigateTo={navigateValueTask} />
</StackPanel>
```

Passing an existing compatible `IAkburaCommand` preserves its identity. A native typed command attribute is different from an arbitrary Avalonia `ICommand` property: do not assume that every `ICommand` preserves the declared argument and result contract.

## Execute and return values

Commands execute through an awaitable `Execute` method:

```akbura
command int Calculate(int value);

var result = await Calculate.Execute(42);
```

Commands with one or more arguments expose a typed runtime interface. A parameterless command currently uses the non-generic `IAkburaCommand` interface, whose `Execute(params object[] args)` returns `ValueTask<object?>`. Cast or pattern-match its result when a parameterless command declares a logical result.

## Async handlers and logical results

Declare the logical result, not the handler's transport type:

```akbura
command UserViewModel Load(int id);

<UserCard Load={async id => {
    return await repository.LoadAsync(id);
}} />
```

The command already executes asynchronously. `command Task<UserViewModel> Load(int id);` therefore means that the `Task<UserViewModel>` itself is the logical value. Akbura reports an informational diagnostic for outer `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>` result types and suggests `void` or `T`. The original task-as-result behavior remains valid when intentional.

## Execution state

Every command exposes:

- `IObservable<bool> IsExecuting`, which reports whether one or more executions are running;
- `IObservable<bool> CanExecute`, which reports current availability;
- `Execute`, which runs the handler asynchronously.

Subscribe with an `IObserver<bool>` and dispose the returned subscription when its owner is disposed. Factory-created commands initially report `false` for `IsExecuting` and `true` for `CanExecute`. Their running state changes only on the execution-count transitions from zero to one and from one to zero.

A direct `Execute` call is not rejected merely because `CanExecute` currently reports `false`; concurrent executions are counted. Handler exceptions flow through the awaited result, and state is restored in `finally`. The factory does not promise queuing, mutual exclusion, automatic cancellation, or exception swallowing.

## Command targets

Keep these targets separate:

- `NavigateTo={...}` on an Akbura component supplies a native declared command handler;
- Avalonia `Button.Command` is an ordinary `ICommand` property;
- `Button.Click={...}` binds a routed event handler.

Use the form required by the target property. Native command handler adaptation does not redefine every Avalonia `ICommand` property or routed event.

## IDE support

Typing `NavigateTo.` offers `Execute`, `CanExecute`, and `IsExecuting`. Completion resolve and signature help display the actual generated runtime signature. Hover on `NavigateTo` displays the source declaration, for example `command void NavigateTo(NavButton button)`, and navigation returns to that declaration rather than to a temporary C# probe.
