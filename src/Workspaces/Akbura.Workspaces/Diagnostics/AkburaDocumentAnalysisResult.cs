using Akbura.Diagnostics;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Diagnostics;

/// <summary>
/// Contains canonical issues and completed analysis stages for one document snapshot.
/// </summary>
public sealed record AkburaDocumentAnalysisResult(
    string DocumentPath,
    AkburaDocumentSnapshotIdentity Snapshot,
    AkburaDiagnosticAnalysisLevel RequestedLevel,
    AkburaDiagnosticAnalysisLevel CompletedLevel,
    bool IsComplete,
    ImmutableArray<AkburaDiagnosticRecord> Diagnostics,
    ImmutableArray<string> IncompleteReasons);
