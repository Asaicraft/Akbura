using Akbura.Diagnostics;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Diagnostics;

/// <summary>
/// Canonical issues retain their source ownership (including locationless issues).
/// Only SourceDiagnostics may be drawn on the requested editor document.
/// </summary>
public sealed record AkburaDiagnosticReport(
    ImmutableArray<AkburaDiagnosticRecord> Diagnostics,
    ImmutableArray<AkburaDiagnosticSpan> SourceDiagnostics);
