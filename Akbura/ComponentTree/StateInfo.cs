namespace Akbura.ComponentTree;

public abstract class StateInfo
{
    protected StateInfo(string name, Type valueType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(valueType);

        Name = name;
        ValueType = valueType;
    }

    public string Name
    {
        get;
    }

    public Type ValueType
    {
        get;
    }

    internal abstract State CreateState(AkburaControl owner);
}
