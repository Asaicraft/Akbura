using System;
using System.ComponentModel;

namespace Akbura.CompilerAnotations;

/// <summary>
/// Describes an AKCSS style, utility, or interceptor exported by a generated
/// module.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AkcssSymbolAttribute : Attribute
{
    public string Name { get; init; } = string.Empty;

    public string MetadataName { get; init; } = string.Empty;

    public AkcssSymbolKind Kind { get; init; }

    public Type? TargetType { get; init; }

    public Type? InterceptType { get; init; }

    public string? ClassName { get; init; }

    public int RuntimeStyleIndex { get; init; } = -1;

    public bool HasErrors { get; init; }
}
