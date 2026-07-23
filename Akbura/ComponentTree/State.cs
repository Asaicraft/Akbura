namespace Akbura.ComponentTree;

public abstract class State
{
    private AkburaControl? _owner;

    internal event Action<State>? ValueChanged;

    internal State()
    {
    }

    public StateInfo? Info
    {
        get; private set;
    }

    public bool IsAttached => _owner != null;

    public abstract Type ValueType
    {
        get;
    }

    internal abstract object? BoxedInitialValue { get; }

    internal abstract object? BoxedValue { get; set; }

    internal void Attach(AkburaControl owner, StateInfo info)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(info);

        if (_owner != null)
        {
            throw new InvalidOperationException(
                "A state instance can only be attached to one component.");
        }

        if (info.ValueType != ValueType)
        {
            throw new ArgumentException(
                $"State info '{info.Name}' describes '{info.ValueType}', " +
                $"not '{ValueType}'.",
                nameof(info));
        }

        _owner = owner;
        Info = info;
    }

    protected void NotifyChanged<T>(Action<T>? subscribers, T value)
    {
        var owner = _owner;
        if (owner == null)
        {
            subscribers?.Invoke(value);
            return;
        }

        owner.BeginStateNotification();
        try
        {
            subscribers?.Invoke(value);
            ValueChanged?.Invoke(this);
        }
        finally
        {
            owner.EndStateNotification();
        }
    }
}
