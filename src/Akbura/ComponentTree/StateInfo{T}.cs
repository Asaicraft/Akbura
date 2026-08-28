namespace Akbura.ComponentTree;

public sealed class StateInfo<T> : StateInfo
{
    private readonly Func<AkburaControl, T>? _initialValueFactory;
    private readonly Func<AkburaControl, State<T>>? _stateFactory;

    public StateInfo(
        string name,
        Func<AkburaControl, T> initialValueFactory)
        : base(name, typeof(T))
    {
        ArgumentNullException.ThrowIfNull(initialValueFactory);

        _initialValueFactory = initialValueFactory;
    }

    private StateInfo(
        string name,
        Func<AkburaControl, State<T>> stateFactory)
        : base(name, typeof(T))
    {
        ArgumentNullException.ThrowIfNull(stateFactory);

        _stateFactory = stateFactory;
    }

    public static StateInfo<T> FromState(
        string name,
        Func<AkburaControl, State<T>> stateFactory)
    {
        return new StateInfo<T>(name, stateFactory);
    }

    internal State<T> CreateTypedState(AkburaControl owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var state = _stateFactory != null
            ? _stateFactory(owner)
            : new State<T>(_initialValueFactory!(owner));
        if (state == null)
        {
            throw new InvalidOperationException(
                $"State factory for '{Name}' returned null.");
        }

        state.Attach(owner, this);
        return state;
    }

    internal override State CreateState(AkburaControl owner)
    {
        return CreateTypedState(owner);
    }
}
