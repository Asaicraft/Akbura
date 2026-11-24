using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Diagnostics;

public interface IAkburaDiagnosticService
{
    /// <summary>
    /// Returns canonical issues without discarding foreign or locationless diagnostics,
    /// together with the source markers belonging to this document and requested span.
    /// </summary>
    AkburaDiagnosticReport GetDiagnosticReport(
        AkburaDocumentContext context,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets diagnostics produced by parsing without waiting for a project or
    /// semantic model.
    /// </summary>
    ImmutableArray<AkburaDiagnosticSpan> GetSyntacticDiagnostics(
        AkburaSyntacticDocument document,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets both syntactic and semantic diagnostics for a project document.
    /// </summary>
    ImmutableArray<AkburaDiagnosticSpan> GetDiagnostics(
        AkburaDocumentContext context,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default);
}
