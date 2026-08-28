using Avalonia.Input;

namespace Akbura.Diagnostics;

/// <summary>
/// Configures the Akbura diagnostics window.
/// </summary>
public sealed class AkburaDiagnosticsOptions
{
    private readonly List<InputBuilder> _inputBuilders;

    public AkburaDiagnosticsOptions()
    {
        ToggleGesture = new KeyGesture(Key.F12);
        _inputBuilders = InputBuilderProvider.CreateDefaultBuilders();
    }

    /// <summary>
    /// Gets or sets the gesture that opens and closes the diagnostics window.
    /// </summary>
    public KeyGesture ToggleGesture { get; set; }

    /// <summary>
    /// Gets the ordered collection of value editors available to diagnostics.
    /// The first compatible editor is selected initially.
    /// </summary>
    /// <remarks>
    /// Insert a custom editor at index zero when it should take precedence over
    /// the built-in editors. The universal text editor is kept as the final
    /// fallback so every value has an editing surface.
    /// </remarks>
    public IList<InputBuilder> InputBuilders => _inputBuilders;

    /// <summary>
    /// Gets or sets an optional service provider passed to value editors.
    /// </summary>
    public IServiceProvider? Services { get; set; }

    internal AkburaDiagnosticsConfiguration CreateConfiguration()
    {
        ArgumentNullException.ThrowIfNull(ToggleGesture);

        if (_inputBuilders.Any(static builder => builder is null))
        {
            throw new InvalidOperationException(
                "Akbura diagnostics input builders cannot contain null values.");
        }

        var builders = _inputBuilders
            .Where(static builder => builder is not UniversalInputBuilder)
            .Append(new UniversalInputBuilder())
            .ToArray();

        return new AkburaDiagnosticsConfiguration(
            ToggleGesture,
            new InputBuilderProvider(builders),
            Services);
    }
}

internal sealed record AkburaDiagnosticsConfiguration(
    KeyGesture ToggleGesture,
    IInputBuilderProvider InputBuilders,
    IServiceProvider? Services);
