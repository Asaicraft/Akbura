using System.Collections.Generic;

namespace Akbura.ComponentTree;

/// <summary>
/// Stores ordinary component states by their stable declaration identity.
/// </summary>
public sealed class StateCollection
{
    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets an existing state or creates it on first access.
    /// </summary>
    /// <param name="key">The stable identity of the state declaration.</param>
    /// <param name="factory">Creates the state when its key is not registered.</param>
    public State<T> GetOrCreate<T>(string key, Func<State<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (_states.TryGetValue(key, out var existing))
        {
            return existing as State<T> ?? throw new InvalidOperationException(
                $"State '{key}' has value type '{existing.ValueType}', not '{typeof(T)}'.");
        }

        var state = factory() ?? throw new InvalidOperationException(
            $"State factory for '{key}' returned null.");
        _states.Add(key, state);
        return state;
    }

    internal void Remove(State state)
    {
        foreach (var entry in _states)
        {
            if (ReferenceEquals(entry.Value, state))
            {
                _states.Remove(entry.Key);
            }
        }
    }
}
