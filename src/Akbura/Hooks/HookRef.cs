namespace Akbura.Hooks;

/// <summary>
/// A stable, nonreactive cell. Assigning Current does not request a render.
/// </summary>
public sealed class HookRef<T>
{
    internal HookRef(T initialValue)
    {
        Current = initialValue;
    }

    public T Current { get; set; }
}
