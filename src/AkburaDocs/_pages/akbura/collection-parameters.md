---
title: Collection Parameters
summary: Stable observable list parameters, resource and data bindings, and collection notification synchronization.
---

# Collection Parameters

A default `param IList` or `param IList<T>` is an owned collection with a writable
**source setter**. The component creates an `ObservableCollection<object>` or
`ObservableCollection<T>` for itself. Assigning another collection copies its items
into that backing and connects notifications; it never replaces the backing object.
Each component instance owns a separate list, including when no value is supplied.

## Declaring a collection parameter

Save this component as `ItemsPanel.akbura`:

```akbura
using Avalonia.Controls;
using System.Collections.Generic;

param IList<int> Data;

<StackPanel>
    <TextBlock Text={$"Count: {Data.Count}"} />
    $foreach (var item in Data)
    {
        <TextBlock Text={$"Item: {item}, index: {@index}"} />
    }
</StackPanel>
```

The public CLR property still has the declared `IList<int>` type. Its getter
always returns the same backing. The setter takes a source of that declared type.
The associated Avalonia property is a **writable direct property**, not a readonly
collection property and not a styled property containing a shared collection default.

## Optional parameter

No empty list has to be passed merely to construct the component:

```akbura
<ItemsPanel />
```

`Data` starts empty. Required non-collection parameters are still required.
The collection is optional in both semantic diagnostics and runtime initialization.
A declared initializer is evaluated once for each backing instance and then supplied
through the same source connection; its collection object is not shared accidentally.

## Observable state source

```akbura
using System.Collections.ObjectModel;

state ObservableCollection<int> items = [1, 2, 3];

<ItemsPanel Data={items} />
```

Adding/removing items in `items` updates the component backing. Editing the backing
updates a writable notifying source as well. Assigning a new source unsubscribes
from the old one, reconciles the contents and subscribes to the new source.
Reassigning the exact same source on a later component update is a no-op: it does
not clear, enumerate or subscribe again. Assigning the backing to itself is also a
no-op; it does not accidentally clear the list or discard its existing connection.

## Static resource source

`Numbers` must resolve to an object assignable to `IList<int>`, for example an
`ObservableCollection<int>` in application resources:

```akbura
<ItemsPanel Data=${StaticResource Numbers} />
```

Static resource lookup and collection observation are separate. Replacing the
resource entry is not a dynamic resource update, but `INotifyCollectionChanged`
events from the resolved collection continue to update the owned list.

## DataContext binding

Here `Items` is an `IList<int>`-compatible property on the inherited DataContext:

```akbura
<ItemsPanel Data=${Binding Items} />
```

The Avalonia binding delivers a source reference to the parameter setter. If the
DataContext implements `INotifyPropertyChanged`, replacing its `Items` property
reconnects the parameter. Reference binding defaults to `OneWay`: the component
must not overwrite the ViewModel property with its own backing list.

This does **not** prevent two-way item synchronization. A writable notifying list
can receive `Add`, `Remove`, `Replace`, `Move` and `Clear` operations from the
component while its ViewModel property continues to reference the original list.
No `Mode=TwoWay` is needed for this content synchronization.

## Non-generic and concrete observable declarations

A non-generic parameter keeps mixed items in `ObservableCollection<object>`:

```akbura
using System.Collections;
using Avalonia.Controls;

param IList Data;

<TextBlock Text={$"Count: {Data.Count}"} />
```

The concrete standard observable type uses the same source-setter behavior:

```akbura
using System.Collections.ObjectModel;
using Avalonia.Controls;

param ObservableCollection<int> Data;

<TextBlock Text={$"Count: {Data.Count}"} />
```

The implementation also preserves existing support for `ICollection<T>` backed
by an observable list. A public concrete `ObservableCollection<T>` subclass with
a public parameterless constructor can be the backing type itself.

A concrete `List<T>`, an arbitrary custom `IList<T>` class or a custom derived
interface cannot be replaced with `ObservableCollection<T>` while preserving its
CLR type. Such declarations are **not** silently rewritten or unsafely cast by
this feature. Prefer `param IList<T>` and pass that custom list as its source.
Legacy concrete `List<T>` parameter generation is unchanged. Explicit `param bind`
and `param out` retain their existing reference-parameter semantics; the automatic
owned-list contract described here applies to unmodified `param` declarations.

## Synchronization contract

| Source object | Source to backing | Backing to source |
| --- | --- | --- |
| Writable `IList<T>` or `IList` implementing `INotifyCollectionChanged` | Event deltas | Yes |
| Readonly notifying list, including `ReadOnlyObservableCollection<T>` | Event deltas | No |
| Fixed-size notifying list | Event deltas | No |
| Non-notifying list or array | Snapshot when the source reference changes | No |
| Enumerable without a writable indexed-list contract | Snapshot; notifying sources refresh on events | No |
| `null` | Disconnect and empty the owned list | No |

Both generic `ICollection<T>.IsReadOnly` and nongeneric `IList.IsReadOnly` /
`IsFixedSize` are considered when present. Arrays cannot support insertion/removal
merely because their individual elements can be assigned.

A readonly source does not make the owned backing readonly. Local edits stay local;
the next source notification makes the backing match the authoritative source again.
For a non-notifying source, use a new list reference to publish a new snapshot. The
runtime connection also exposes `Refresh()` for explicit refresh by custom hosts;
ordinary component updates do not poll a non-notifying list of unchanged identity.

Ordered deltas use indexes, not `Remove(value)`. Duplicate values and equal-but-distinct
reference objects remain separate occurrences. A standard single-item `Move` remains a
`Move` on the owned observable collection. Range notifications are translated into
indexed operations. `Reset`, unusable indexes and a divergent readonly projection use
a fresh source snapshot, with linear positional reconciliation instead of unconditional
`Clear` plus `Add`. Reset, an unaddressable notification and explicit refresh relay
replacement notifications even for unchanged references: an object may have changed
without changing identity. Replacing the source with an equivalent snapshot does not
produce those forced replacements. Normal indexed Add/Remove/Move events leave unrelated
items untouched.

New source items are validated before the old connection is removed. A heterogeneous
nongeneric source containing incompatible elements is rejected. This bridge is not a
transaction system for arbitrary user collections: a source method or an external
collection-event handler can throw after mutating the source. Such failures are not
reported as successful synchronization. A source altered by another listener during
a reverse write is reconciled after the owned `CollectionChanged` event unwinds.
Indexed source-to-backing deltas do not enumerate the complete source. Reverse
writes currently validate a source snapshot afterwards; that validation is linear
in the source size, even when the outgoing mutation itself was one indexed operation.

## Foreach and component lifetime

`$foreach` already observes the **source object's `INotifyCollectionChanged` contract**;
it does not require a concrete `ObservableCollection<T>`. The parameter bridge checks
that same interface, including generic-only custom lists. The loop subscribes to the
stable owned list and receives its deltas without a new visual/logical wrapper.
An unrelated parameter update does not force the bridge to enumerate or reset its source.

The bridge schedules a component update after collection notifications have been
delivered. This also refreshes expressions such as `Data.Count` outside the loop.
It does not guarantee that every loop body is skipped: `@index`, environment reads,
`break`, keys and body dependencies still follow the normal foreach rules.

The source subscription is removed on component detachment and replaced on source
assignment. On reattachment, the current source is authoritative and refreshed; local
edits made while detached are not replayed back into it. A weak source listener prevents
an application-scoped resource from retaining a discarded component. Collection events
and mutation must occur on the owning UI thread; the bridge does not enumerate a list
concurrently on a worker thread.

Compatible Hot Reload updates retain the direct-property identity, owned list and source
connection. The writable-source descriptor has a new contract key so it is not confused
with an older readonly collection descriptor. Changing/removing the parameter contract
is not treated as a compatible source update.

## Collection content

`Content` collections and collection property elements still use the generated Add
helper and the same owned backing. Clearing/resetting collection content now synchronizes
logical children rather than throwing the old "Resetting component content" exception.
Avoid configuring the same destination simultaneously from source assignment and markup
children unless appending those children into a writable bound source is intentional.

See [Foreach Markup](/akbura/foreach) for keys, indexes, early exits and rendering behavior.
