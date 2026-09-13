---
title: Composable Hooks
summary: State, derived values, timers, asynchronous loading and subscriptions built from Akbura hook primitives.
---

Hooks run during each render and compose the existing `useHookState` and `useEffect`
primitives. Import `Akbura.Hooks` in `.akbura` or C#; C# also supports
`control.useEffect(...)`, `control.useState(...)` and the other extension calls.

## State and derived values

```akbura
using System;
using Akbura.Hooks;

state int count = useState(0);
state int doubled = useSelect(count, x => x * 2);
state int offset = 10;
state int combined = useComputed(() => count + offset, [count, offset]);
state int previous = usePrevious(count, -1);
```

`useState` is an alias for `useHookState`, with initial-value, lazy-initializer and
`StateInfo<T>` overloads. Initializers run only for new slots, and may be retried
after an aborted first render. Declare a source state before the hook that consumes
it: a `State<T>` parameter receives the live object without `.Source` or `.State`.

`useComputed` calculates the initial result once, then recalculates after a
successful render with changed dependencies. Dependencies are copied and compared
using element-wise default equality. `useSelect` depends on source identity and
source value; replacing an inline selector alone does not trigger recalculation.
For additional captured inputs, use `useComputed` and list them explicitly.

Derived values update through effects. A source change can therefore require a
further settling render, and a chain of derived values can require several passes.
For a simple expression in markup, `{count * 2}` computes directly during render.

`useDistinct(source, comparer)` suppresses updates equivalent to the current result,
in addition to the default equality check performed by `State<T>`. Replacing the
comparer is a dependency change.

`usePrevious` remembers the previous different source value, not the previous
render's value. `useOnChange(source, (before, after) => ...)` skips the initial
value. Both reset history when source identity changes and preserve it through
temporary detach. Aborted renders do not advance their history.

## Refs, latest values and IDs

```akbura
state HookRef<int> lastSaved = useRef(0);
state HookRef<int> latestCount = useLatest(count);
state string fieldId = useId("search");
```

`HookRef<T>.Current` is a mutable cell; assigning it does not request a render.
`useLatest` updates the cell after successful commit, so render itself can still
see the previous committed value. Use it in callbacks rather than as a replacement
for a computed state.

`useId` returns a GUID-based ID stable for one hook lifetime. Its initial prefix
does not change on rerender. IDs change when Hot Reload resets the hook slots and
are not intended for persistence or list keys.

## Update effects

```akbura
useUpdateEffect(() => { Console.WriteLine(count); }, [count]);
```

This effect skips its first callback in each attached lifetime, including after
reattach. It accepts `Action`, `Func<Action?>` cleanup callbacks and
`Func<CancellationToken, Task>` async callbacks. Dependencies do not reset the
initial-skip flag. Existing `useEffect` owns cancellation and cleanup.

## Debounce and throttle

```akbura
state int delayed = useDebounce(count, 300);
state int limited = useThrottle(count, 300);
state int limitedPlusOne = useThrottle(count, x => x + 1, 300);
useThrottle(() => { Console.WriteLine(count); }, 300, [count]);
```

Debounce waits for a quiet period. Throttle queues a leading publication, then
coalesces updates within each interval into one trailing publication using the
last pending callback. With continuous updates, throttle keeps publishing rather
than repeatedly postponing its deadline. An interval with no new input does not
produce a duplicate trailing call.

Throttle accepts milliseconds or `TimeSpan`, from 1 ms through `int.MaxValue`
milliseconds. An interval change starts a new series. Its callback form accepts
synchronous `Action`; use `useInterval` for awaited periodic work.

## Timeout and interval

```akbura
state bool running = true;
state int ticks = 0;
state bool showHint = true;

useTimeout(() => { showHint = false; }, 3000);
useInterval(() => { ticks++; }, running ? 1000 : (int?)null);
```

Both accept `Action` or `Func<CancellationToken, Task>`, with `int?` milliseconds
or `TimeSpan?`. `null` pauses work without removing the hook from the sequence.
Cast a literal null to select its overload. Negative delays are invalid. Timeout
allows zero and still queues its callback on the UI dispatcher; interval requires
at least 1 ms. Maximum delay is `int.MaxValue` milliseconds.

`useTimeout(callback, delay, dependencies)` restarts when a dependency or delay
changes. Without dependencies it runs once for its delay and attached lifetime.
Interval uses a fixed delay: wait a full interval, await the callback, then wait
another full interval. One run neither overlaps its own callbacks nor catches up
missed ticks. Unrelated renders update the committed callback without resetting
the deadline.

Cancellation prevents delayed or queued callbacks. Already-running async code must
cooperate with the token, including checking it before external side effects.

## Async loading

```akbura
using System.Threading.Tasks;
using Akbura.Hooks;

state string query = "";
state string debouncedQuery = useDebounce(query, 350);
state AsyncSnapshot<string> response = useAsync(async cancellationToken =>
{
    var text = debouncedQuery;
    await Task.Delay(200, cancellationToken);
    return text.ToUpperInvariant();
}, [debouncedQuery]);
```

`AsyncSnapshot<T>` exposes `IsLoading`, `HasValue`, `Value` and `Error`.
`HasValue` distinguishes no result from a successful null or default value.
The initial snapshot is empty; the first effect starts loading. By default a
restart retains previous data and clears the previous error. Pass
`keepPreviousValue: false` to clear data while loading.

Dependencies select a new run; a reload counter in the list enables explicit
retry. Old runs are canceled, and canceled results cannot publish even if a loader
ignores its token. Loader failures, null tasks and uncanceled
`OperationCanceledException` appear in `Error`. Results publish on the UI thread.
Loading begins on the UI thread too; CPU-intensive work is not automatically
offloaded.

## External sources and events

`useObservable(stream, initialValue, onError)` subscribes after commit and disposes
the subscription when source identity changes or the component detaches. Worker
notifications reach the UI thread; queued notifications from canceled
subscriptions are ignored. Completion and switching streams retain the last value.
The latest committed error callback does not recreate the subscription; without
one, errors are raised on the UI thread.

```csharp
var snapshot = control.useExternalStore(
    changed => store.Subscribe(changed),
    () => store.GetSnapshot(),
    [store]);

control.useEventListener<EventArgs>(
    handler => publisher.Changed += handler,
    handler => publisher.Changed -= handler,
    (sender, args) => RefreshUi(),
    [publisher]);
```

External stores read an initial snapshot, subscribe, then read again to close the
gap between render and subscription. Cleanup also runs if that second read fails.
The getter is captured by the subscribing run: list store/filter changes in the
dependencies. Event listeners use the latest committed handler without duplicate
subscriptions, and remove exactly the delegate that was added.

## Reducers and lifecycle

```akbura
state HookReducer<int, int> counter =
    useReducer<int, int>((value, delta) => value + delta, 0);

<Button Click={() => counter.Dispatch(1)}>+1</Button>
<Button Click={() => counter.Reset()}>Reset</Button>
<TextBlock Text={counter.Value.ToString()} />
```

Reducers own private state and a stable `HookReducer` object. Replacing a reducer
function takes effect only after successful commit. Dispatch and Reset belong on
the UI thread in handlers or effects, not during render. Reducers must be pure;
recursive Dispatch/Reset from a reducer is rejected. Reset returns the original
initial value rather than recalculating an initializer.

All hooks use the existing positional sequence. Keep their order and count stable;
pause a timer with null rather than conditionally omitting it. Create state during
render and mutate it from handlers/effects. User mutations performed during render
are not transactional.

Detach stops effects and preserves hook-state values. Reattach starts resources
again. Hot Reload resets hook-owned state under the existing policy; ordinary
component state follows its own preservation rules.
