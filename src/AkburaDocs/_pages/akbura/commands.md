# Commands

Akbura commands are typed operations declared by a component and supplied by its parent. They can also be invoked from ordinary Avalonia `ICommand` properties without writing a command class or calling a factory in markup.

## Declare a command contract

This complete child component declares the operation that its parent must supply. The inline handler assigned to `Button.Command` is adapted to `ICommand` and runs only when Avalonia executes the button command:

```akbura
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;

namespace Demo;

param bool IsActive = false;
param IList<StreamGeometry> Geometries;
command void NavigateTo(NavButton button);

<Button Command={async () => await NavigateTo.Execute(this)}>
    Navigate
</Button>
```

`this` is the current `Demo.NavButton`. It is not the inner `Button` or a temporary compiler object.

The parent supplies the declared command handlers:

```akbura
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;

namespace Demo;

<StackPanel>
    <NavButton
        Geometries={Array.Empty<StreamGeometry>()}
        NavigateTo={button => {
            button.IsActive = true;
        }} />

    <NavButton
        Geometries={Array.Empty<StreamGeometry>()}
        NavigateTo={() => {
            Console.WriteLine("Navigate without using the argument");
        }} />

    <NavButton
        Geometries={Array.Empty<StreamGeometry>()}
        NavigateTo={async button => {
            await Task.Yield();
            button.IsActive = true;
        }} />
</StackPanel>
```

A zero-argument lambda may intentionally ignore the declared command arguments. Native command attributes also accept compatible method groups, typed delegates, `Task`/`ValueTask` handlers, and compatible `IAkburaCommand` instances.

## Adapt a handler to an ordinary ICommand property

Any supported writable property whose exact contract is `System.Windows.Input.ICommand` or `ICommand?` can receive a callable:

```akbura
<StackPanel>
    <Button Command={() => Save()}>Save</Button>
    <Button Command={Save}>Save with a method group</Button>
    <Button Command={async () => await SaveAsync()}>Save asynchronously</Button>
    <Button Command={(Document document) => Open(document)}
            CommandParameter={selectedDocument}>
        Open
    </Button>
</StackPanel>
```

This works for Avalonia properties, CLR properties, and Akbura component parameters. It is based on the property type, not on the name `Command` or on `Button`.

Supported callables have zero or one parameter and include `Action`, `Func<T>`, `Func<Task>`, `Func<Task<T>>`, `Func<ValueTask>`, `Func<ValueTask<T>>`, their one-parameter forms, compatible method groups, lambdas, and anonymous methods. A result is evaluated when the command runs. At the ordinary `ICommand` boundary Akbura awaits one `Task` or `ValueTask` layer and discards the logical result.

An invocation that already produces a value is not reinterpreted as a delayed handler. For example, `Command={GetExistingCommand()}` evaluates the factory during property assignment, while `Command={SaveAsync()}` remains a type error. Use `Command={SaveAsync}` or `Command={() => SaveAsync()}` for a delayed async handler.

Properties with a more specific contract, such as a concrete `MyCommand` or an `IMyCommand : ICommand` interface with additional members, are not adapted unless the assigned value already has a normal supported conversion to that type.

## Command parameters

A parameterless handler ignores `CommandParameter`, including `null`. One implicitly typed parameter is `object?`:

```akbura
<Button Command={parameter => Log(parameter)}
        CommandParameter={currentValue} />
```

An explicit parameter or a typed delegate selects `T`:

```akbura
<Button Command={(NavButton button) => Select(button)}
        CommandParameter={this} />
```

`CanExecute(parameter)` returns `false` when the value is incompatible with `T`. Calling `Execute` directly with an incompatible value throws `ArgumentException` without invoking the handler. `null` is accepted for reference types and `Nullable<T>`, but not for a non-nullable value type. Akbura does not parse strings, use `Convert.ChangeType`, or dynamically invoke the handler. An `object[]` command parameter is one value; it is not expanded into multiple arguments.

## Pass a ready command directly

Values already assignable to `ICommand` keep their identity and behavior:

```akbura
<StackPanel>
    <Button Command={viewModel.SaveCommand} />
    <Button Command={GetExistingCommand()} />
    <Button Command=${Binding SaveCommand} />
    <Button Command={null} />
</StackPanel>
```

An Akbura command can also be passed directly:

```akbura
<Button Command={NavigateTo} CommandParameter={this}>
    Navigate
</Button>
```

In this form `ReferenceEquals(button.Command, NavigateTo)` is true. The `CommandParameter` is passed to the declared command's typed runtime bridge.

Use the inline adapter when you need to compose behavior, for example `async () => await NavigateTo.Execute(this)`. Use direct assignment when the existing command's identity, subscriptions, and business `CanExecute` behavior must be preserved.

## Execute and logical results

Declared commands expose an awaitable `Execute` method:

```akbura
command int Calculate(int value);

var result = await Calculate.Execute(42);
```

Declare the logical result, not the handler's transport type:

```akbura
command UserViewModel Load(int id);

<UserCard Load={async id => {
    return await repository.LoadAsync(id);
}} />
```

`command Task<UserViewModel> Load(int id);` means that the `Task<UserViewModel>` itself is the logical value. Akbura reports an informational diagnostic for outer `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>` result types and suggests `void` or `T`; the task-as-result contract remains valid when intentional.

Commands with one or more arguments expose a typed runtime interface. A parameterless command currently uses the non-generic `IAkburaCommand` interface, whose `Execute(params object[] args)` returns `ValueTask<object?>`.

## Execution state

A declared Akbura command exposes:

- `IObservable<bool> IsExecuting`;
- `IObservable<bool> CanExecute`;
- an awaitable `Execute` method.

The compiler-owned adapter used for an ordinary `ICommand` property exposes the standard `ICommand.CanExecute` and `CanExecuteChanged` contract. It reports `false` while one or more executions are running and returns to `true` after completion. A retained control keeps the same adapter during rerender and Hot Reload, including its busy state; future executions use the latest captured handler, while an operation already in progress keeps the callback with which it started.

The inline adapter has its own execution availability. It does not automatically inherit a business `CanExecute` policy from code called inside the lambda. Directly assign the ready command when that policy must be preserved.

Neither command path promises a queue, mutual exclusion, automatic cancellation, background execution, or exception swallowing. A direct awaited `IAkburaCommand.Execute` exposes exceptions to its caller. The ordinary `ICommand.Execute` entry point follows the platform's void command invocation model.

## IDE support

Typing `NavigateTo.` offers `Execute`, `CanExecute`, and `IsExecuting`. Completion inside a parent native handler uses the declared command parameter type. Inside an ordinary `ICommand` handler, an implicit parameter is `object?`; an explicitly typed parameter uses that type.

Completion resolve and signature help display the generated runtime signature. Hover on `NavigateTo` displays the source declaration, and navigation returns to that declaration rather than to a temporary C# probe.
