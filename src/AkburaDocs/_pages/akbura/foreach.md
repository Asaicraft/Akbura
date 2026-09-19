---
title: Foreach Markup
summary: Repeat markup directly in mutable lists with observable sources, stable keys, and source indexes.
---

# Foreach Markup

`$foreach` repeats markup directly inside a parent's collection. The statement
is not a control: it adds no visual or logical wrapper, `ItemsControl`, or item
container. In a `StackPanel`, the controls declared by the loop become direct
children of that panel, alongside any ordinary or conditional siblings.

::: warning Version compatibility
This page describes the current repository implementation. Use matching
compiler, runtime, and editor versions that include `$foreach`. Support in an
older published alpha package is not implied.
:::

For owned lists declared with `param IList` or `param IList<T>`, see
[Collection Parameters](/akbura/collection-parameters). The loop observes the
stable backing list; the parameter manages its external source connection.

## Basic syntax

```akbura
using Avalonia.Controls;
using System.Collections.Immutable;

state ImmutableArray<int> array = [1, 2, 3, 4, 5];

<StackPanel Spacing="8">
    $foreach (var item in array)
    {
        <TextBlock Text={$"Current item is {item}"} />
    }
</StackPanel>
```

The iteration variable is inferred from the source, or its type can be written
explicitly, as in `$foreach (int item in array)`. It is available in the body
and the optional key expression, but not in its own source expression or after
the loop.

Supported headers use synchronous value iteration with one named variable over
an `IEnumerable<T>` or non-generic `IEnumerable` source. Asynchronous iteration,
`ref`/`ref readonly` iteration, deconstruction headers, and ref-like iteration
values are not supported.

An iteration can contribute no children, one child, or several children. An
empty source contributes nothing; a `null` source and a default
`ImmutableArray<T>` are also treated as empty.

Use the iteration variable in expressions such as `Text={person.Name}`. A loop
is not an `ItemTemplate` and does not automatically set each child's
`DataContext` to its current item. Explicit bindings retain their normal
Avalonia data-context rules.

## Where a loop can be placed

A loop requires an accessible, mutable, indexed **destination** implementing
`IList<T>` or non-generic `IList`. The destination's item type must accept the
values produced by the body. A getter-only property is supported when it
returns a mutable list; the loop changes the list, not the property itself.

Both implicit content and property-element content are supported:

```akbura
using Avalonia.Controls;
using System.Collections.Immutable;

state ImmutableArray<int> array = [1, 2, 3];

<StackPanel>
    <StackPanel.Children>
        <TextBlock Text="Before the loop" />

        $foreach (var item in array)
        {
            <TextBlock Text={$"Item {item}"} />
        }

        <TextBlock Text="After the loop" />
    </StackPanel.Children>
</StackPanel>
```

This is not limited to `Control` collections: a custom `IList<int>` property,
for example, can receive inline `{item}` values of the matching type.

A scalar property is not a supported destination. The following is invalid,
even though the source contains exactly one item:

```akbura
// Invalid: Border.Child accepts one Control, not an indexed list.
<Border>
    $foreach (var item in new[] { 1 })
    {
        <TextBlock Text={$"Item {item}"} />
    }
</Border>
```

Write a `StackPanel` inside the `Border` and put the loop inside that panel when
a panel is the intended layout. Akbura does not insert one automatically.

Dictionary, array, read-only, fixed-size, and Add-only destinations are not
supported. Implementing only `ICollection<T>` is not enough. When a property's
declared interface hides the actual list's mutability, the runtime validates
that destination before applying the loop.

These restrictions concern the **output collection**, not the source. Arrays,
`ImmutableArray<T>`, and read-only collections can still be enumerable sources.
A loop cannot replace the component's top-level scalar root.

## Observable sources

Use `ObservableCollection<T>` for a list that changes in place:

```akbura
using Avalonia.Controls;
using System.Collections.ObjectModel;

state ObservableCollection<int> array = [1, 2, 3, 4, 5];

<StackPanel Spacing="8">
    <Button Click={() => array.Add(array.Count + 1)}>Add an item</Button>
    <TextBlock Text="Starting loop" />

    $foreach (var item in array)
    {
        <TextBlock Text={$"Current item is {item}"} />
    }

    <TextBlock Text="Ending loop" />

    $if (array.Count > 10)
    {
        <TextBlock Text="A lot of items!" />
    }
</StackPanel>
```

The initial render reads the source and builds its output. After that, Akbura
uses the source's `INotifyCollectionChanged` notifications rather than requiring
you to assign a new collection after each mutation. The source object itself
is checked for this interface, including when it is exposed as `IEnumerable<T>`.

| Change | Effect on iterations |
|--------|----------------------|
| `Add` | Creates records for the inserted occurrences and renders those that execution reaches. |
| `Remove` | Removes the corresponding occurrences and releases their owned output. |
| `Move` | Moves existing occurrences, retaining their compatible controls. |
| `Replace` | Updates the affected occurrence; a changed explicit key gives it a new identity. |
| `Reset` | Reads a new snapshot and reconciles it, using keys when available. |

Changing the collection also schedules an owner update, so surrounding
expressions such as `array.Count > 10` can change with it. Updates are scheduled;
do not rely on a source mutation synchronously completing the UI update before
`Add` or `Remove` returns. `ObservableCollection<T>` uses `Count`, not `Length`.

Keep the observable source as the loop's source when notification-based updates
are needed. A derived LINQ sequence such as `array.Where(...)` is a different
source and does not automatically forward the original collection's events.
For filtering in place, the loop body can use `continue`.

### Threading and lifetime

Mutate an observed collection and its observed item properties on the Avalonia
UI thread. A background operation should dispatch its resulting changes to
that thread. Do not mutate the source while the loop is evaluating it.

Assigning a different source makes the region switch its subscription and
reconcile the new data. Source and item-property subscriptions are released
when their region is removed or disposed. Temporarily suspended regions stop
observing changes and refresh their state when resumed; no manual
`CollectionChanged` subscription is needed for ordinary markup usage.

## Immutable and other enumerable sources

For immutable state, assign the new value returned by the collection operation:

```akbura
using Avalonia.Controls;
using System.Collections.Immutable;

state ImmutableArray<int> array = [1, 2, 3];

<StackPanel>
    <Button Click={() => array = array.Add(array.Length + 1)}>Add an item</Button>

    $foreach (var item in array; key: item)
    {
        <TextBlock Text={$"Item {item}"} />
    }
</StackPanel>
```

This example uses unique integers, so each value can also be its iteration key.
For records with editable values, prefer a stable identifier such as `person.Id`.

An unchanged `ImmutableArray<T>` can reuse its cached source records. This does
not freeze the body: changes to its dependencies or Hot Reload can still require
reevaluation. If an earlier pass stopped at `break`, the runtime remembers that
only a prefix was read and replays the source when the execution path must be
updated. It does not read past `break` merely to cache the rest of a plain source.

Other non-notifying enumerables are enumerated on applicable component renders.
Mutating an ordinary `List<T>` does not itself send a collection notification
or request that render. Prefer an observable collection for in-place changes,
or replace immutable state. Plain enumerable sources should be safe to enumerate
again; they are not a one-shot stream consumed for the lifetime of the component.

## Iteration identity

Notifications and keys solve different problems. Notifications describe what
changed. A key identifies which retained iteration corresponds to an item when
the runtime needs to match a new snapshot.

### Explicit keys

Add `; key: expression` after the source:

```akbura
using Avalonia.Controls;
using System.Collections.Immutable;

state ImmutableArray<int> array = [1, 2, 3];

<StackPanel>
    $foreach (var item in array; key: item)
    {
        <TextBlock Text={$"Item {item}"} />
        <TextBlock Text={$"Square: {item * item}"} />
    }
</StackPanel>
```

The header key identifies the **whole iteration**, including both root
controls. It is the preferred form for bodies containing multiple roots,
local declarations, conditions, or early exits.

For a source whose item type exposes `Id` and `Name`, the same syntax is
`$foreach (var person in people; key: person.Id)` with `Text={person.Name}` in
the body. The key should remain stable when other item values change.

Keys must be non-null and unique within that loop region. The generated key
contract uses `EqualityComparer<TKey>.Default`. A duplicate or runtime-null key
fails the update; it does not silently merge two occurrences.

Iteration keys are restricted to side-effect-free metadata of the current item
and constants. `item` and `item.Id` are typical choices. The compiler rejects
keys based on `index`, ambient state, assignments, increments, allocations, and
method calls, even when a helper method appears pure. For example,
`key: GetId(item)` is not a substitute for `key: item.Id`. Property getters used
as keys must themselves be stable and free of side effects.

### The `x.id` shorthand

A loop with exactly one direct root element and no other body statements can
put the key on that root:

```akbura
using Avalonia.Controls;
using System.Collections.Immutable;

state ImmutableArray<int> array = [1, 2, 3];

<StackPanel>
    $foreach (var item in array)
    {
        <TextBlock Text={$"Item {item}"} x.id={item} />
    }
</StackPanel>
```

When there is no explicit header key, this root's `x.id` becomes the iteration
key. Formatting whitespace does not prevent that shorthand. Adding another
root or a local/conditional statement changes that rule: the compiler does not
pick the first `x.id` as the key of an arbitrary body.

In a multi-root body, or when the header already declares a key, an `x.id` on a
root identifies that root declaration within the iteration rather than the
whole iteration. Use the header key to keep the iteration's identity explicit
as its body grows.

`x.id` is compiler metadata, not a CLR property setter. It is different from
`x.Name`, which declares an element name, and `x.key`, which describes an entry
in a dictionary. An element may declare `x.id` only once.

### Without a key

Equal values are allowed in a keyless source: `[7, 7, 7]` describes three
occurrences, not one. Indexed observable notifications let Akbura retain the
specific occurrence involved in a `Move` without comparing its value to every
other item.

When there is no event history to apply, keyless snapshot matching is
positional. Use stable keys when a reordered or replaced snapshot must retain
controls by item identity instead of by position.

A key does not promise keep-alive after removal, and removing an item then
adding it again is not the same operation as `Move`. Nor does a key prevent
property updates: a retained control still receives current item values and
its current index when those expressions need updating.

## The source index

`@index` is the read-only, zero-based index of the current occurrence in the
**source sequence**. It is not the child's position in the destination list and
is not renumbered to remove gaps left by `continue`:

```akbura
using Avalonia.Controls;
using System.Collections.Immutable;

state ImmutableArray<int> array = [1, 2, 3, 4, 5];

<StackPanel>
    $foreach (var item in array)
    {
        if (item % 2 == 0)
        {
            continue;
        }

        <TextBlock Text={$"Item {item}, source index {@index}"} />
    }
</StackPanel>
```

This produces:

```text
Item 1, source index 0
Item 3, source index 2
Item 5, source index 4
```

All roots from one iteration share the same index. Inserting or moving items
can require updating index-dependent properties of retained controls without
recreating them.

The identifier is also available as `index`; `@index` makes its loop role clear
in markup expressions. Both spellings refer to the same read-only symbol.
Do not assign it, redeclare it, or name the iteration variable `index`.
`nameof(index)` remains `"index"`; text inside strings and member names such as
`person.@index` are not rewritten into the loop index.

## Local code, `continue`, and `break`

The direct body of `$foreach` is mixed markup and a supported subset of C#.
It accepts local declarations, ordinary `if`/`else if`/`else`, `continue`, and
`break`, as well as markup and nested `$foreach` statements:

```akbura
using Avalonia.Controls;
using System.Collections.ObjectModel;

state ObservableCollection<int> array = [1, 2, 3, 4, 5];

<StackPanel>
    <TextBlock Text="Starting loop" />

    $foreach (var item in array)
    {
        var doubled = item * 2;

        if (item % 2 == 0)
        {
            continue;
        }

        if (item % 3 == 0)
        {
            break;
        }

        <TextBlock Text={$"Item {item}, doubled {doubled}, index {@index}"} />
    }

    <TextBlock Text="Ending loop" />
</StackPanel>
```

With these values, the body renders only item `1`: item `2` is skipped, and item
`3` stops the loop. The following `Ending loop` sibling is still rendered.

`continue` skips the rest of the current iteration. `break` skips the rest of
that iteration and all later iterations of that loop. Neither operation removes
markup that was already reached earlier in the current iteration. For example,
a `TextBlock` written before a guard remains in the output when that guard
executes `break`.

An observable change can alter which iterations are reached. Removing the item
that caused `break`, changing its observed data, or changing a condition's
component dependencies may require evaluating a previously hidden suffix.
Incremental notification handling does not make that necessary evaluation
disappear.

Local variables and pattern variables follow their C# scope and definite
assignment rules. For example, `if (item is not Person person) { continue; }`
allows following expressions in that iteration to use `person`.

Use ordinary `if` for guards in the mixed body. Inside a nested element's
markup body, use [conditional markup](/akbura/conditional-markup) with `$if`
instead. The loop's direct code context does not turn ordinary text inside a
child control into C#.

A loop body is a repeatable description of UI, not an imperative initialization
script. Arbitrary expression statements, `return`, `for`, `while`, `switch`,
`try`, and component declarations are not supported as direct loop-body
statements. Put actions such as modifying a collection in event handlers or
component methods, not in the render path. Keep parent hooks outside the loop.

## Nested loops

A nested loop has its own iteration variable and source index. Outer locals
remain available. Capture the outer index in a local when both indexes are
needed:

```akbura
using Avalonia.Controls;
using System.Collections.Immutable;

state ImmutableArray<int> rows = [1, 2];

<StackPanel>
    $foreach (var row in rows)
    {
        var rowIndex = @index;

        $foreach (var column in new[] { 10, 20 })
        {
            <TextBlock Text={$"Row {row} ({rowIndex}), column {column} ({@index})"} />
        }
    }
</StackPanel>
```

The inner loop contributes directly to the same destination in this example.
It does not create a row panel. Add an explicit panel per outer iteration when
that is the layout you need. `break` and `continue` belong to the enclosing
markup loop, so an inner `break` does not stop the outer loop.

## Item changes and update cost

A collection notification describes changes to its entries, not every change
inside its objects. For direct member reads on an item type known to implement
`INotifyPropertyChanged`, the compiler can request item-property observation;
the runtime then refreshes occurrences for the changed object. An ordinary
non-notifying property setter cannot request an update on its own. This is not
a general deep watcher for every object reachable from an item.

The work needed also depends on the body. Reading `@index` can affect a suffix
after an insertion. Reading `array.Count` in every row can affect every reached
row. Component state, calls with unknown effects, and changed `break` conditions
can also require reevaluating more than the newly added item.

Reevaluating an expression, enumerating the source, creating a control, and
changing the destination list are separate operations. Compatible controls can
be retained while their properties change. A property-only update does not by
itself require rewriting the outer list's membership. However, the current
runtime still processes internal records and child sequences; notification
support is not a guarantee that every update is constant-time.

`$foreach` does not provide viewport virtualization. It creates the output of
the iterations that execution reaches, even when those controls are outside a
scrolling viewport. See [ItemsControl](/akbura/items-control) for the separate
item-template model; a wrapper-free loop is not a virtualizing items control.

## Hot Reload

For a retained loop, Hot Reload updates the body separately from the iteration
key contract. Editing a `Text` expression does not by itself turn an unchanged
`key: item.Id` into a different key contract. Compatible keyed iterations can
therefore retain their controls while the new body is applied.

Changing a control type or the actual key contract may require replacement.
Changes to `break`, `continue`, and other body conditions require reevaluating
the reached output, even if the collection sent no event. Removing a loop
releases the resources it owns; it does not clear the entire parent collection.

These rules concern a retained declaration and compatible nodes. They are not
a promise that every structural edit, every removed iteration, or every
changed declaration identity keeps the same control instances.

## Editor support

The shared Workspaces services provide `$foreach` completion, C# scope for its
header and body expressions, and source-mapped symbol information. The iteration
variable, locals, and loop index participate in that scope. Braces and
parentheses participate in pairing, indentation, and folding through the editor
integration.

For related language rules, see [Conditional Markup](/akbura/conditional-markup),
[Reactive State](/#reactive-state), [Binding](/#binding), and
[ItemsControl](/akbura/items-control).
