using Akbura.Workspaces;
using Akbura.Workspaces.Projects;
using Akbura.Workspaces.Utilities;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Akbura.Mcp;

[McpServerToolType]
internal sealed class McpTools(ProjectSession session)
{
    public const string Instructions =
        "Call akbura_project_info before working on the UI. Utilities are markup attributes. " +
        "Use akbura_find_utilities to check exact names and applicability for sizing, spacing, visibility, and layout. " +
        "Prefer a matching standard utility when it expresses the intended behavior, and respect its target and required imports. " +
        "Variants use the form ${variant}:utility. Verify that the project's references support the variant before using it. " +
        "Use ordinary properties or custom styles when they express the behavior more clearly.";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static string ServerVersion { get; } =
        typeof(McpTools).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        typeof(McpTools).Assembly.GetName().Version?.ToString() ?? "unknown";

    [McpServerTool(Name = "akbura_project_info", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description("Describe the selected MSBuild project, saved documents, resolved dependencies, and standard AKCSS imports.")]
    public async Task<ProjectInfoResponse> ProjectInfoAsync(CancellationToken cancellationToken)
    {
        var view = await session.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var diagnostics = new List<ToolDiagnostic>();
        foreach (var diagnostic in session.LoadDiagnostics)
        {
            diagnostics.Add(new("msbuild_" + diagnostic.Severity.ToString().ToLowerInvariant(), diagnostic.Message));
        }

        var project = view.Snapshot?.GetRequiredProject(session.Workspace.DefaultProjectId);
        var workspaceVersion = typeof(AkburaWorkspace).Assembly.GetName().Version?.ToString() ?? "unknown";
        if (project == null)
        {
            diagnostics.Add(new(view.Status, view.Message!));
            return new(session.Options.ProjectPath, session.Settings.Configuration, session.Settings.TargetFramework,
                view.Status, ServerVersion, AkburaWorkspace.CompilerVersion, workspaceVersion, "unknown",
                [], [], null, null, null, false, diagnostics.ToArray(),
                new(Instructions, null, null, []));
        }

        var documentPaths = new List<string>();
        var akcssCount = 0;
        foreach (var document in project.Documents.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(document.FilePath);
            if (extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase))
            {
                documentPaths.Add(Path.GetRelativePath(project.Context.ProjectDirectory, document.FilePath));
            }
            else if (extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase))
            {
                akcssCount++;
            }
        }
        documentPaths.Sort(StringComparer.Ordinal);

        var library = session.Workspace.LanguageServices.UtilityCatalog.GetStandardLibrary(project, cancellationToken);
        if (library.Diagnostic is { } libraryDiagnostic)
        {
            diagnostics.Add(new(libraryDiagnostic.Code, libraryDiagnostic.Message));
        }

        var assemblyVersions = new List<DependencyVersion>();
        foreach (var identity in project.CSharpCompilation.ReferencedAssemblyNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identity.Name == "Akbura" || identity.Name == "Avalonia" ||
                identity.Name.StartsWith("Avalonia.", StringComparison.Ordinal))
            {
                assemblyVersions.Add(new(identity.Name, identity.Version.ToString()));
            }
        }
        assemblyVersions.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));

        var guidance = library.SupportsGridBreakpointExample
            ? new UtilityGuidance(Instructions,
                "<Border col-span-12 ${md}:col-span-6 />",
                "Border is an Avalonia Control. The example sets its column span when placed in a Grid.",
                ["using Avalonia.Controls;", library.UtilityImport!, library.VariantImport!])
            : new UtilityGuidance(Instructions, null, null, []);
        cancellationToken.ThrowIfCancellationRequested();
        return new(session.Options.ProjectPath, session.Settings.Configuration, session.Settings.TargetFramework,
            view.Status, ServerVersion, AkburaWorkspace.CompilerVersion, workspaceVersion, "unknown",
            session.Packages.ToArray(), assemblyVersions.ToArray(),
            new(documentPaths.Count, akcssCount, documentPaths.ToArray()),
            new(library.IsAvailable, library.UtilityImport, library.VariantImport),
            view.Snapshot!.Version.ToString(), IsLoadComplete, diagnostics.ToArray(), guidance);
    }

    [McpServerTool(Name = "akbura_find_utilities", UseStructuredContent = true, OutputSchemaType = typeof(UtilitySearchResponse),
        ReadOnly = true, OpenWorld = false)]
    [Description("Discover real AKCSS utilities applicable to a target in a saved, registered .akbura document.")]
    public async Task<CallToolResult> FindUtilitiesAsync(
        [Description("Project-relative or absolute path of a saved .akbura document in the selected project.")]
        string documentPath,
        [Description("Target type name resolved with project references and document imports. Supply this or position.")]
        string? targetType = null,
        [Description("Zero-based absolute UTF-16 offset inside a markup element. Supply this or targetType.")]
        int? position = null,
        [Description("Utility name, prefix, category, or a supported English or Russian keyword.")]
        string? query = null,
        [Description("Category filter, such as layout, spacing, grid, or appearance.")]
        string? category = null,
        bool includeImportable = true,
        [Range(1, 100)] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            return Error("invalid_limit", "Limit must be between 1 and 100.");
        }
        if (position.HasValue == (targetType != null) || (targetType != null && string.IsNullOrWhiteSpace(targetType)))
        {
            return Error("invalid_target_selection", "Specify exactly one of position and targetType.");
        }
        if (string.IsNullOrWhiteSpace(documentPath))
        {
            return Error("unknown_document", "Supply the path of a registered .akbura document.");
        }

        var view = await session.CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (view.Snapshot is not { } snapshot)
        {
            return Error(view.Status, view.Message!);
        }

        var project = snapshot.GetRequiredProject(session.Workspace.DefaultProjectId);
        var snapshotId = snapshot.Version.ToString();
        string path;
        try
        {
            path = Path.GetFullPath(documentPath, project.Context.ProjectDirectory);
        }
        catch (ArgumentException)
        {
            return Error("unknown_document", "The document path is invalid.", snapshotId);
        }
        catch (NotSupportedException)
        {
            return Error("unknown_document", "The document path is invalid.", snapshotId);
        }

        if (!Path.GetExtension(path).Equals(".akbura", StringComparison.OrdinalIgnoreCase) ||
            !project.TryGetDocument(new Uri(path), out var document) ||
            !snapshot.TryGetDocumentContext(document.Id, out var context))
        {
            return Error("unknown_document", "The document is not a registered .akbura input of the selected project.", snapshotId);
        }

        var request = new AkburaUtilitySearchRequest(position, targetType, query, category, includeImportable, limit);
        var result = session.Workspace.LanguageServices.UtilityCatalog.FindUtilities(context, request, cancellationToken);
        var status = result.Status switch
        {
            AkburaUtilitySearchStatus.Success => "success",
            AkburaUtilitySearchStatus.InvalidDocument => "unknown_document",
            AkburaUtilitySearchStatus.InvalidTargetSelection => "invalid_target_selection",
            AkburaUtilitySearchStatus.InvalidPosition => "invalid_position",
            AkburaUtilitySearchStatus.UnresolvedTarget => "unresolved_target",
            AkburaUtilitySearchStatus.AmbiguousTarget => "ambiguous_target",
            AkburaUtilitySearchStatus.MissingReferences => "missing_references",
            _ => throw new InvalidOperationException("Unrecognized utility search status."),
        };
        var diagnostics = new List<ToolDiagnostic>(result.Diagnostics.Length + session.LoadDiagnostics.Length);
        foreach (var diagnostic in result.Diagnostics)
        {
            diagnostics.Add(new(diagnostic.Code, diagnostic.Message));
        }
        foreach (var diagnostic in session.LoadDiagnostics)
        {
            diagnostics.Add(new("msbuild_" + diagnostic.Severity.ToString().ToLowerInvariant(), diagnostic.Message));
        }
        var utilities = new UtilityResponse[result.Utilities.Length];
        for (var index = 0; index < utilities.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            utilities[index] = Map(result.Utilities[index]);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Format(new(status, result.ResolvedTargetType, utilities, result.SnapshotId,
            result.IsComplete && IsLoadComplete, result.Truncated, diagnostics.ToArray()));
    }

    private bool IsLoadComplete => !session.LoadDiagnostics.Any(static diagnostic =>
        diagnostic.Severity != AkburaProjectLoadDiagnosticSeverity.Information);

    private static UtilityResponse Map(AkburaUtilityDescriptor utility)
    {
        var parameters = new UtilityParameterResponse[utility.Parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = utility.Parameters[index];
            parameters[index] = new(parameter.Name, parameter.Type, parameter.IsOptional);
        }

        var location = utility.SourceLocation == null ? null :
            new UtilityLocationResponse(utility.SourceLocation.FilePath, utility.SourceLocation.Start, utility.SourceLocation.Length);
        return new(utility.Id, utility.Name, utility.UsageSyntax, parameters, utility.DeclaredTargetType,
            utility.Availability == AkburaUtilityAvailability.Available ? "available" : "requires_import",
            utility.RequiredImports.ToArray(),
            utility.OriginKind == AkburaUtilityOriginKind.Source ? "source" : "library",
            utility.Origin, location, utility.Category, utility.Description, utility.Example);
    }

    private static CallToolResult Error(string status, string message, string? snapshotId = null) =>
        Format(new(status, null, [], snapshotId, IsComplete: false, Truncated: false, [new(status, message)]));

    private static CallToolResult Format(UtilitySearchResponse response)
    {
        var success = response.Status == "success";
        return new CallToolResult
        {
            IsError = !success,
            StructuredContent = JsonSerializer.SerializeToElement(response, JsonOptions),
            Content = [new TextContentBlock
            {
                Text = success
                    ? $"Found {response.Utilities.Length} applicable utility definitions."
                    : response.Diagnostics[0].Message,
            }],
        };
    }
}
