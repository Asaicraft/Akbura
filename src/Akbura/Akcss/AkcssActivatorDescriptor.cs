using System.ComponentModel;

namespace Akbura.Akcss;

/// <summary>
/// Describes one Debug declaration without constructing its runtime activator.
/// Identity is a declaration slot; Family and SyntaxIdentity allow unambiguous
/// matching when neighboring declarations move or a declaration's value changes.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public readonly record struct AkcssActivatorDescriptor(
    string Identity,
    string Family,
    string SyntaxIdentity,
    string? Revision);
