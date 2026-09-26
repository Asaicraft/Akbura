---
title: Reactive State
summary: Local state, directional bind, out and in connections, binding paths, lifecycle and editor behavior.
---

# Reactive state

`state` declares a reactive value owned by one component instance. The type may
be explicit or inferred from the initializer:

```akbura
state count = 0;
state int selectedIndex = 0;
state string query = "";
```

The initializer runs when that state is first created. A normal render caused by
another value does not recreate it. Assigning an equal value is ignored by the
default equality comparer. A normal initializer is a snapshot, not a computed
state: `state int doubled = count * 2;` does not recalculate when `count` changes.
Use a markup expression or a [composable hook](/akbura/hooks) for a derived value.

## Directional connections

A mode immediately after `=` connects a state value to a C# binding path:

```akbura
state MyViewModel vm = new MyViewModel();

state string? name = bind vm.Name;
state string fullName = out vm.FullName;
state string? surname = in vm.Surname;
```

| Declaration | Data flow | Can component code assign the state? |
| --- | --- | --- |
| `state T value = expression;` | initializer → state once | Yes |
| `state T value = bind source.Path;` | source ↔ state | Yes |
| `state T value = out source.Path;` | source → state | No |
| `state T value = in target.Path;` | state → target | Yes |

`bind` reads the initial source value, follows source notifications, and writes
state changes back. After a write it reads the target again, so a normalizing
setter such as `value.Trim()` becomes the final state value instead of causing an
echo loop.

`out` follows the source but exposes a shallow-readonly state to component code.
Assignments, compound assignments, increment/decrement, deconstruction, `ref`
and `out` writes are errors, including inside handlers and hook callbacks. This
does not make an object stored in the state deeply immutable.

`in` writes later state assignments to the current target. When the target has an
accessible getter, its value is read once for compatible initialization; later
target changes do not create a reverse channel. A write-only target starts at
`default(T)` and its setter is not called merely by creating the connection. For
a write-only target the state type must be explicit because there is no readable
value from which to infer `T`. For a nullable/reference type, the state can
therefore be `null` until the first component assignment.

## Binding paths and observation

Directional initializers accept identifiers, member access and indexers:

```akbura
state string name = bind vm.Name;
state string surname = in vm.People[0].Surname;
state string title = out vm.Current.Title;
```

An arbitrary calculation is not a path. Keep it as a normal initializer or use a
derived hook:

```akbura
state int snapshot = vm.Age + 1;
// state int invalid = bind vm.Age + 1;
```

Akbura observes `INotifyPropertyChanged`, Avalonia property changes, and
`INotifyCollectionChanged` for indexed segments. A plain `List<T>` or an ordinary
getter cannot announce every mutation. `bind` and `out` report the existing
non-observable-source warning when no supported notification channel is present.

Replacing an observable root, intermediate object, or collection item rebuilds
the path. Events from the old owner are disconnected. If a root or intermediate
value is `null`, Akbura does not create an object and does not write to the old
owner. Read directions retain the last successful value (or `default(T)` before
the first value). Writes performed while an `in`/`bind` target is disconnected
are not queued or replayed; after reconnection, `bind` reads the current target,
while `in` waits for the next different state assignment.

## Observable sources

`out` may consume `IObservable<T>` directly:

```akbura
state string message = out vm.Messages;
```

The value visible in Akbura is `T`; the observable object is only the source.
Before the first `OnNext`, the value is `default(T)`. `OnCompleted` retains the
last value and releases the subscription. `OnError` releases the subscription
and rethrows the same exception to the producer's call path; it is not swallowed
or converted into a target write. A normal `state stream = vm.Messages;` stores
the stream itself and does not subscribe.

A pure `IObservable<T>` is not a writable channel, so it cannot be used for
`bind` or `in` merely because it can publish values.

## Runtime ownership and Hot Reload

The connection belongs to the state and component, not to a particular control
that reads it. Detaching a component suspends owned subscriptions; reattaching
restores one connection to the current source. Akbura never disposes the view
model, collection, or observable itself.

An unrelated Hot Reload edit preserves compatible local state. The connection
identity includes its mode and path, so changing `bind` to `out`, or changing the
path, replaces the old binding resources. A state type change uses the existing
incompatible-state policy instead of casting `State<OldT>` to `State<NewT>`.

## State values and hooks

In ordinary Akbura expressions, `count` has its value type such as `int`, not the
technical runtime type `State<int>`, and no `.Value` access is required. When an
existing hook parameter explicitly expects `State<T>`, the compiler passes the
same live state object according to the hook contract:

```akbura
using Akbura.Hooks;

state int count = 0;
state int delayed = useDebounce(count, 300);
```

An `out` state cannot be passed to a hook parameter that exposes mutable
`State<T>` access; use a value/read-only hook contract instead.

The editor offers `bind`, `out`, and `in` only in the mode position after `=`.
After a mode it switches to normal typed C# path completion. Hover shows the
value type, direction, and readonly status. Ordinary explicitly typed
initializers keep their expected type, including target-typed `new()` and lambda
parameters.
