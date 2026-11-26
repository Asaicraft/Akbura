namespace Akbura.Mcp;

/// <summary>
/// Describes the selected project's load state, documents, dependencies, and catalogs.
/// </summary>
internal sealed record ProjectInfoResponse(
    string ProjectPath,
    string Configuration,
    string TargetFramework,
    string Status,
    string ServerVersion,
    string CompilerVersion,
    string WorkspaceVersion,
    string LanguageCompatibility,
    DependencyVersion[] PackageVersions,
    DependencyVersion[] AssemblyVersions,
    ProjectDocumentSummary? Documents,
    StandardLibraryResponse? StandardLibrary,
    string? SnapshotId,
    bool IsComplete,
    ToolDiagnostic[] Diagnostics,
    UtilityGuidance Guidance,
    RecipeSummaryResponse[] LayoutRecipes);
