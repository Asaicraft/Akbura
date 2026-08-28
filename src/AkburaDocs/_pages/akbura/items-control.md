---
title: ItemsControl
summary: Render collections with typed item templates, compiled bindings, x.ItemName, and x.DataType.
---

Avalonia's `ItemsControl` renders a collection by creating template content for every item.

In Akbura, assign a collection to `ItemsSource` and declare the item template through the `ItemsControl.ItemTemplate` property element:

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate>
        <TextBlock Text=${Binding Name} />
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

For every object in `Vm.Items`, Avalonia creates one `TextBlock`.

Akbura provides two ways to access the current item inside a template:

1. A compiled binding such as `${Binding Name}`.
2. A typed C# variable declared with `x.ItemName`.

Both approaches can use a type inferred automatically from `ItemsSource` or provided explicitly through `x.DataType`.

## Example model

The examples on this page use the following types:

```csharp
namespace Demo.Models;

public sealed class TaskItem
{
    public int Id { get; init; }

    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    public bool IsCompleted { get; init; }
}
```

```csharp
namespace Demo.ViewModels;

public sealed class TasksViewModel
{
    public IReadOnlyList<TaskItem> Items { get; init; } = [];
}
```

The view model can be injected or stored in state:

```akbura
using Demo.ViewModels;

inject TasksViewModel Vm;
```

## Basic item template

The shortest form uses a compiled binding:

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate>
        <TextBlock Text=${Binding Title} />
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

Inside `ItemTemplate`, the binding source is the current item rather than the component itself.

Therefore:

```akbura
<TextBlock Text=${Binding Title} />
```

reads `Title` from the current `TaskItem`.

Conceptually, this template is applied once for every item:

```text
Vm.Items[0] -> TextBlock
Vm.Items[1] -> TextBlock
Vm.Items[2] -> TextBlock
```

## Automatic item type inference

Akbura attempts to determine the item type from `ItemsSource`.

In this example:

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate>
        <TextBlock Text=${Binding Title} />
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

`Vm.Items` has the type:

```csharp
IReadOnlyList<TaskItem>
```

Because that type implements `IEnumerable<TaskItem>`, Akbura infers that the template data type is `TaskItem`.

The compiled binding is therefore checked as though its source were a `TaskItem`:

```csharp
TaskItem item;
var value = item.Title;
```

This provides compile-time validation of the binding path.

For example, this is valid:

```akbura
<TextBlock Text=${Binding Title} />
```

This produces a compiler diagnostic because `TaskItem` has no `FullName` property:

```akbura
<TextBlock Text=${Binding FullName} />
```

Akbura can infer item types from:

* arrays such as `TaskItem[]`;
* `IEnumerable<T>`;
* types implementing `IEnumerable<T>`;
* typed C# expressions assigned to `ItemsSource`;
* typed binding expressions whose result type is known.

## Compiled bindings

The `${Binding ...}` syntax accesses the current template item through an Avalonia binding.

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate>
        <StackPanel>
            <TextBlock Text=${Binding Title} />
            <TextBlock Text=${Binding Description} />
        </StackPanel>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

Binding paths may contain nested properties:

```akbura
<TextBlock Text=${Binding Author.DisplayName} />
```

They may also contain supported indexers:

```akbura
<TextBlock Text=${Binding History[0].Title} />
```

The compiler resolves every path segment against the inferred or explicitly provided item type.

A compiled binding is useful when the value should continue to follow the item's property through Avalonia's binding system.

## `x.ItemName`

Use `x.ItemName` when the template needs a direct typed C# reference to the current item:

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate x.ItemName="item">
        <TextBlock Text={item.Title} />
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

The value of `x.ItemName` becomes a local variable inside the template.

In this example, Akbura effectively introduces:

```csharp
TaskItem item;
```

The variable is available to expressions inside the template:

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate x.ItemName="item">
        <StackPanel>
            <TextBlock Text={item.Title} />

            <TextBlock
                Text={item.IsCompleted
                    ? "Completed"
                    : "Not completed"} />
        </StackPanel>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

Because `item` is a typed C# value, normal C# syntax can be used:

```akbura
<TextBlock Text={item.Title.ToUpperInvariant()} />
```

```akbura
<TextBlock Text={$"Task #{item.Id}: {item.Title}"} />
```

```akbura
<Border IsVisible={!item.IsCompleted} />
```

### Event handlers

The item variable can also be captured by an event handler:

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate x.ItemName="item">
        <Button Click={() => OpenTask(item.Id)}>
            {item.Title}
        </Button>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

The expression is type-checked using the actual item type.

For example, if `Id` is an `int`, the compiler knows that:

```akbura
OpenTask(item.Id)
```

passes an `int`.

### Item name rules

The value of `x.ItemName` must be a valid C# identifier.

Valid names include:

```akbura
x.ItemName="item"
x.ItemName="task"
x.ItemName="user"
x.ItemName="currentItem"
```

Invalid names include:

```akbura
x.ItemName="current-item"
x.ItemName="1item"
x.ItemName="current item"
```

`x.ItemName` is a compile-time directive. It does not set an Avalonia property and does not assign a name to the generated control.

## `x.ItemName` and `x.Name`

`x.ItemName` and `x.Name` serve different purposes.

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate x.ItemName="item">
        <Button
            x.Name="itemButton"
            Content={item.Title} />
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

Here:

* `item` refers to the current object from `ItemsSource`;
* `itemButton` refers to the generated `Button`.

| Directive    | Refers to                                  |
| ------------ | ------------------------------------------ |
| `x.ItemName` | The current template data item             |
| `x.Name`     | A control created by markup                |
| `x.DataType` | The compile-time type of the template item |

## `x.DataType`

Use `x.DataType` to specify the template item type explicitly:

```akbura
using Demo.Models;

<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate
        x.DataType="TaskItem">
        <TextBlock Text=${Binding Title} />
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

`x.DataType` is especially useful when Akbura cannot infer the type from `ItemsSource`.

It may also be combined with `x.ItemName`:

```akbura
using Demo.Models;

<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate
        x.DataType="TaskItem"
        x.ItemName="item">

        <TextBlock Text={item.Title} />

    </ItemsControl.ItemTemplate>
</ItemsControl>
```

The declared item variable now has the explicit type:

```csharp
TaskItem item;
```

### Qualified type names

A type imported through `using` can be written using its short name:

```akbura
using Demo.Models;

<ItemsControl.ItemTemplate x.DataType="TaskItem">
```

A fully qualified type name may also be used:

```akbura
<ItemsControl.ItemTemplate
    x.DataType="Demo.Models.TaskItem">
```

The global namespace qualifier can be used when necessary:

```akbura
<ItemsControl.ItemTemplate
    x.DataType="global::Demo.Models.TaskItem">
```

## When `x.DataType` is required

Automatic inference works only when the compiler can obtain a concrete generic item type.

For example, inference works here:

```akbura
state IReadOnlyList<TaskItem> tasks = [];

<ItemsControl ItemsSource={tasks}>
    <ItemsControl.ItemTemplate x.ItemName="item">
        <TextBlock Text={item.Title} />
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

The compiler can see `IEnumerable<TaskItem>` and infer `TaskItem`.

Inference may not work when `ItemsSource` is typed as `object`:

```akbura
state object tasks = GetTasks();
```

```akbura
<ItemsControl ItemsSource={tasks}>
    <ItemsControl.ItemTemplate x.ItemName="item">
        <TextBlock Text={item.Title} />
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

The static type of `tasks` does not expose `IEnumerable<T>`, so Akbura cannot determine the type of `item`.

Provide it explicitly:

```akbura
using Demo.Models;

<ItemsControl ItemsSource={tasks}>
    <ItemsControl.ItemTemplate
        x.DataType="TaskItem"
        x.ItemName="item">

        <TextBlock Text={item.Title} />

    </ItemsControl.ItemTemplate>
</ItemsControl>
```

The same applies to a non-generic collection whose element type is unknown at compile time.

::: info
Akbura uses the static compile-time type of `ItemsSource`.

The objects stored in the collection at runtime do not affect compile-time type inference.

## Explicit type takes priority

When `x.DataType` is present, it takes priority over automatic inference.

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate
        x.DataType="TaskItem"
        x.ItemName="item">

        <TextBlock Text={item.Title} />

    </ItemsControl.ItemTemplate>
</ItemsControl>
```

Even when `Vm.Items` already exposes `IEnumerable<TaskItem>`, the explicitly declared `TaskItem` type is used as the template data type.

This can make template contracts clearer, but an incorrect explicit type may cause bindings or expressions to be checked against the wrong type.

## Choosing between bindings and `x.ItemName`

Use a compiled binding for direct property access:

```akbura
<TextBlock Text=${Binding Title} />
```

Use `x.ItemName` when you need a C# expression:

```akbura
<TextBlock Text={item.Title.ToUpperInvariant()} />
```

Use `x.ItemName` for conditions:

```akbura
<TextBlock
    Text={item.IsCompleted ? "Completed" : "Pending"} />
```

Use `x.ItemName` in event handlers:

```akbura
<Button Click={() => DeleteTask(item.Id)}>
    Delete
</Button>
```

Both approaches may be used inside the same template:

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate x.ItemName="item">
        <StackPanel>
            <TextBlock Text=${Binding Title} />

            <TextBlock
                Text={$"Identifier: {item.Id}"} />

            <Button Click={() => OpenTask(item)}>
                Open
            </Button>
        </StackPanel>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

## Complex item layouts

The template may contain a complete control tree:

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate x.ItemName="item">
        <Border
            Padding="12"
            Margin="0,0,0,8">

            <Grid ColumnDefinitions="*, Auto">
                <StackPanel>
                    <TextBlock Text={item.Title} />
                    <TextBlock Text={item.Description} />
                </StackPanel>

                <TextBlock
                    Grid.Column="1"
                    Text={item.IsCompleted ? "Done" : "Pending"} />
            </Grid>

        </Border>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

If one item requires several sibling controls, place them inside a panel such as:

* `StackPanel`;
* `Grid`;
* `DockPanel`;
* another suitable Avalonia control.

## Bindings in `ItemsSource`

`ItemsSource` may itself use an Avalonia binding:

```akbura
<ItemsControl ItemsSource=${Binding Items}>
    <ItemsControl.ItemTemplate>
        <TextBlock Text=${Binding Title} />
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

When the result type of the outer binding is known, Akbura can use it to infer the template item type.

For example, if `Items` has the type:

```csharp
IReadOnlyList<TaskItem>
```

the template data type is inferred as `TaskItem`.

When the outer binding does not expose a concrete generic collection type, provide `x.DataType` explicitly.

## Nested `ItemsControl`

Each item template has its own item scope.

Suppose a group contains a collection of tasks:

```csharp
public sealed class TaskGroup
{
    public string Name { get; init; } = "";

    public IReadOnlyList<TaskItem> Tasks { get; init; } = [];
}
```

Nested collections can use separate item names:

```akbura
<ItemsControl ItemsSource={Vm.Groups}>
    <ItemsControl.ItemTemplate x.ItemName="group">
        <StackPanel>
            <TextBlock Text={group.Name} />

            <ItemsControl ItemsSource={group.Tasks}>
                <ItemsControl.ItemTemplate x.ItemName="task">
                    <TextBlock Text={task.Title} />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

Inside the inner template:

* `task` refers to the current `TaskItem`;
* `group` remains the outer template item.

Using distinct item names makes nested template expressions easier to understand.

## Common errors

### The item variable is not found

```akbura
<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate>
        <TextBlock Text={item.Title} />
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

`item` was never declared.

Add `x.ItemName`:

```akbura
<ItemsControl.ItemTemplate x.ItemName="item">
```

### The compiler cannot determine the item type

```akbura
state object items = GetItems();

<ItemsControl ItemsSource={items}>
    <ItemsControl.ItemTemplate x.ItemName="item">
        <TextBlock Text={item.Title} />
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

The static type of `items` is `object`.

Add `x.DataType`:

```akbura
<ItemsControl.ItemTemplate
    x.DataType="TaskItem"
    x.ItemName="item">
```

### The compiled binding property is not found

```akbura
<TextBlock Text=${Binding FullName} />
```

If the item type has no `FullName` property, Akbura reports an invalid compiled binding path.

Use an existing property:

```akbura
<TextBlock Text=${Binding Title} />
```

### `x.ItemName` contains an invalid identifier

```akbura
<ItemsControl.ItemTemplate x.ItemName="current-item">
```

Use a valid C# identifier:

```akbura
<ItemsControl.ItemTemplate x.ItemName="currentItem">
```

## Complete example

```akbura
using Avalonia.Controls;
using Demo.Models;
using Demo.ViewModels;

inject TasksViewModel Vm;

void OpenTask(TaskItem task)
{
    Console.WriteLine($"Opening task {task.Id}");
}

<ItemsControl ItemsSource={Vm.Items}>
    <ItemsControl.ItemTemplate
        x.DataType="TaskItem"
        x.ItemName="task">

        <Border
            Padding="12"
            Margin="0,0,0,8">

            <Grid ColumnDefinitions="*, Auto">
                <StackPanel>
                    <TextBlock Text=${Binding Title} />

                    <TextBlock
                        Text={task.Description} />
                </StackPanel>

                <Button
                    Grid.Column="1"
                    Click={() => OpenTask(task)}>
                    Open
                </Button>
            </Grid>

        </Border>

    </ItemsControl.ItemTemplate>
</ItemsControl>
```

In this example:

1. `Vm.Items` supplies the collection.
2. `x.DataType="TaskItem"` explicitly declares the item type.
3. `x.ItemName="task"` creates a typed C# variable.
4. `${Binding Title}` reads the current item through a compiled binding.
5. `{task.Description}` uses a direct C# expression.
6. The button event captures the current `task`.