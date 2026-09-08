using Avalonia;
using System.ComponentModel;

namespace Akbura.HotReload;

/// <summary>
/// Associates a stable generated descriptor key with its Avalonia property.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public readonly struct AkburaHotReloadPropertyRegistration
{
    /// <summary>
    /// Initializes a generated descriptor registration.
    /// </summary>
    /// <param name="key">The stable descriptor identity.</param>
    /// <param name="property">The Avalonia property registered for the descriptor.</param>
    public AkburaHotReloadPropertyRegistration(
        string key,
        AvaloniaProperty property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(property);

        Key = key;
        Property = property;
    }

    /// <summary>
    /// Gets the stable descriptor identity.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the registered Avalonia property.
    /// </summary>
    public AvaloniaProperty Property { get; }
}
