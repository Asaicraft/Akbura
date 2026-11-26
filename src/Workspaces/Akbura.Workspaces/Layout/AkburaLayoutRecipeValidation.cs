using Akbura.Diagnostics;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Layout;

/// <summary>
/// Reports the semantic analysis of the standalone reference example in the captured project.
/// </summary>
public sealed record AkburaLayoutRecipeValidation(
    AkburaLayoutRecipeValidationStatus Status,
    string? ExamplePath,
    AkburaDiagnosticAnalysisLevel? CompletedLevel,
    ImmutableArray<AkburaDiagnosticRecord> Diagnostics);
