namespace Akbura.Mcp;

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
    UtilityGuidance Guidance);

internal sealed record ProjectDocumentSummary(int AkburaCount, int AkcssCount, string[] AkburaPaths);

internal sealed record StandardLibraryResponse(
    bool IsAvailable,
    string? UtilityImport,
    string? VariantImport);

internal sealed record UtilityGuidance(
    string Instructions,
    string? Example,
    string? ExampleContext,
    string[] ExampleImports);

internal sealed record UtilitySearchResponse(
    string Status,
    string? ResolvedTargetType,
    UtilityResponse[] Utilities,
    string? SnapshotId,
    bool IsComplete,
    bool Truncated,
    ToolDiagnostic[] Diagnostics);

internal sealed record UtilityResponse(
    string Id,
    string Name,
    string UsageSyntax,
    UtilityParameterResponse[] Parameters,
    string? DeclaredTargetType,
    string Availability,
    string[] RequiredImports,
    string OriginKind,
    string Origin,
    UtilityLocationResponse? SourceLocation,
    string? Category,
    string? Description,
    string? Example);

internal sealed record UtilityParameterResponse(string Name, string Type, bool IsOptional);

internal sealed record UtilityLocationResponse(string FilePath, int Start, int Length);

internal sealed record ToolDiagnostic(string Code, string Message);
