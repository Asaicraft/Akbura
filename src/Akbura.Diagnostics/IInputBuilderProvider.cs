using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Diagnostics;

/// <summary>
/// Selects value editors for a diagnostics input request.
/// </summary>
public interface IInputBuilderProvider: IReadOnlyList<InputBuilder>
{
    /// <summary>
    /// Returns the preferred compatible editor.
    /// </summary>
    public InputBuilder Provide(InputRequest inputRequest);

    /// <summary>
    /// Returns all compatible editors in selection order.
    /// </summary>
    public IEnumerable<InputBuilder> Provides(InputRequest inputRequest);
}
