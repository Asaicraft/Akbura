using System.ComponentModel;

namespace Akbura.CompilerAnotations;

/// <summary>
/// Identifies the declaration represented by an AKCSS metadata carrier.
/// </summary>
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public enum AkcssSymbolKind
{
    Style,
    Utility,
    Intercept,
}
