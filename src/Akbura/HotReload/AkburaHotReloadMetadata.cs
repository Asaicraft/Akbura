using System.Collections.Immutable;
using System.ComponentModel;

namespace Akbura.HotReload;

/// <summary>
/// Stores the last applied generated shapes and property manifest outside the component type.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaHotReloadMetadata
{
    internal AkburaHotReloadMetadata(
        string descriptorShape,
        string stateShape,
        ImmutableArray<AkburaHotReloadPropertyRegistration> properties)
    {
        UpdateDescriptors(descriptorShape, properties);
        UpdateStateShape(stateShape);
    }

    /// <summary>
    /// Gets the last successfully rebuilt descriptor shape.
    /// </summary>
    public string DescriptorShape { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the state shape used for the last scheduled component refresh.
    /// </summary>
    public string StateShape { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the property manifest belonging to the applied descriptor shape.
    /// </summary>
    public ImmutableArray<AkburaHotReloadPropertyRegistration> Properties { get; private set; }

    /// <summary>
    /// Records a descriptor shape and manifest after a successful descriptor rebuild.
    /// </summary>
    /// <remarks>
    /// Generated metadata updates run on the Avalonia UI thread.
    /// </remarks>
    public void UpdateDescriptors(
        string descriptorShape,
        ImmutableArray<AkburaHotReloadPropertyRegistration> properties)
    {
        ArgumentNullException.ThrowIfNull(descriptorShape);
        if (properties.IsDefault)
        {
            throw new ArgumentException("A property manifest must be initialized.", nameof(properties));
        }

        Properties = properties;
        DescriptorShape = descriptorShape;
    }

    /// <summary>
    /// Records the state shape used when scheduling a component refresh.
    /// </summary>
    /// <remarks>
    /// Generated metadata updates run on the Avalonia UI thread.
    /// </remarks>
    public void UpdateStateShape(string stateShape)
    {
        ArgumentNullException.ThrowIfNull(stateShape);
        StateShape = stateShape;
    }
}
