namespace Akbura.Mcp;

/// <summary>
/// Reports the semantic validation scope and outcome for the standalone example.
/// </summary>
internal sealed record RecipeValidationResponse(
    string Status,
    string? CompletedLevel,
    string? ExamplePath,
    string Scope,
    bool HasRepositoryRuntimeTests,
    bool IntegratedDocumentValidated,
    bool ProjectRuntimeValidated,
    CanonicalDiagnosticResponse[] Diagnostics);
