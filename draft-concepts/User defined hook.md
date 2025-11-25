# User Defined Hooks

> **Note:** This feature comes from `draft-concepts` and may change or never be implemented.
> It represents early exploration of extending Akbura using custom hooks.

Akbura may support user-defined hooks that allow developers to insert reusable logic into component state updates. A user-defined hook acts similarly to React hooks, but is integrated directly into Akbura’s state system.

---

## Hook Definition Rules

A hook must:

* Have a name that **starts with `Use`** (e.g., `UseDebounce`, `UseName`).
* Be annotated with the `[UserHook]` attribute.
* Provide a `UseHook` method that describes how it transforms state or data.

Hooks can:

* Wrap existing state
* Subscribe to updates
* Produce new derived state
* Provide metadata or helper values

---

## Example: Debounce Hook

```csharp
[UserHook]
public struct UseDebounceHook
{
    public State<T> UseHook<T>(AkburaComponent component, StateInfo<T> state, int debounce)
    {
        var debouncedState = new State<T>(state.DefaultValue);

        state.Subscribe(async (value, cancellationToken) =>
        {
            await Task.Delay(debounce, cancellationToken);

            AkburaScheduler.Invoke(component,
                () => debouncedState.Value = value,
                cancellationToken);
        });

        return debouncedState;
    }
}
```

This hook creates a **derived state** that updates only after no further changes occur for the given number of milliseconds.

---

## Example: State Name Hook

```csharp
[UserHook]
public struct UseNameHook
{    
    public string UseHook<T>(AkburaComponent component, StateInfo<T> state)
    {
        return state.FieldName ?? "Unnamed State";
    }
}
```

This hook simply returns the field name of a state variable.

---

## Using Hooks in `.akbura` Code

```akbura
state string query = "";
state string debouncedQuery = useDebounce(300, query);
state string name = useName(query);

<Stack>
    <Text>Type your query:</Text>
    <Input bind:value={query} placeholder="Enter name..." />

    <Text>Query name: {name}</Text>
    <Text>Debounced query: {debouncedQuery}</Text>
</Stack>
```

### Explanation

* `useDebounce(300, query)` returns a derived state that updates *300ms after typing stops*.
* `useName(query)` returns metadata describing the state variable.

---

## Status

This system is experimental and under design consideration.
It explores a possible direction where Akbura developers can define reusable logic patterns analogous to hooks in React, but with stronger type integration and full compile-time support.