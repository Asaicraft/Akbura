using System;
using System.ComponentModel;

namespace Akbura.CompilerAnotations;

/// <summary>
/// Describes a parameter accepted by an exported AKCSS utility.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AkcssUtilityParameterAttribute : Attribute
{
    public int Ordinal { get; init; }

    public string Name { get; init; } = string.Empty;

    public Type Type { get; init; } = typeof(object);

    public string? CSharpName { get; init; }

    public bool IsOptional { get; init; }
}
