using Avalonia;
using System.ComponentModel;

namespace Akbura.ComponentTree;

/// <summary>Creates reactive command properties declared by generated components.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public static class CommandProperty
{
    /// <summary>Registers a command property that requests a component update when its value changes.</summary>
    /// <typeparam name="TOwner">The component type that declares the command.</typeparam>
    /// <param name="name">The command and styled property name.</param>
    /// <returns>The command property. The caller should cache this registration.</returns>
    public static StyledProperty<IAkburaCommand> Create<TOwner>(string name)
        where TOwner : AkburaControl
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

#pragma warning disable AVP1001 // The same AvaloniaProperty should not be registered twice
        var property = AvaloniaProperty.Register<TOwner, IAkburaCommand>(name);
#pragma warning restore AVP1001 // The same AvaloniaProperty should not be registered twice

        property.Changed.AddClassHandler<TOwner>(static (owner, _) => owner.InvalidState());
        return property;
    }
}
