using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Diagnostics;

/// <summary>
/// Describes a component value for which diagnostics needs an editor.
/// </summary>
public sealed record InputRequest
{
    /// <summary>
    /// Gets the type declared by the state or parameter.
    /// </summary>
    public required Type RequestedType { get; init; }

    /// <summary>
    /// Gets the inspected component type.
    /// </summary>
    public required Type ComponentType { get; init; }

    /// <summary>
    /// Gets the kind of inspected data.
    /// </summary>
    public required DataVariation Variation { get; init; }

    /// <summary>
    /// Gets the state or parameter name, when available.
    /// </summary>
    public string? MemberName { get; init; }

    /// <summary>
    /// Gets the inspected component instance, when available.
    /// </summary>
    public object? ComponentInstance { get; init; }

    /// <summary>
    /// Gets optional application services supplied through diagnostics options.
    /// </summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>
    /// Gets the value present when the editor is created.
    /// </summary>
    public object? ExistingValue { get; init; }

    /// <summary>
    /// Gets the concrete type that should be preserved by an editor.
    /// </summary>
    public Type EditorType =>
        ExistingValue is not null &&
        (RequestedType == typeof(object) ||
         RequestedType.IsAbstract ||
         RequestedType.IsInterface)
            ? ExistingValue.GetType()
            : RequestedType;
}
