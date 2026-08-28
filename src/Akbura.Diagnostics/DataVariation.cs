using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Diagnostics;

/// <summary>
/// Identifies the kind of component data represented by an input request.
/// </summary>
public enum DataVariation
{
    /// <summary>A component parameter.</summary>
    Parameter,

    /// <summary>A component state.</summary>
    State,

    /// <summary>An injected service.</summary>
    Service
}
