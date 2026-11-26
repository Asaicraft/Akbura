using Akbura.Workspaces.Diagnostics;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Akbura.Mcp;

internal sealed partial class McpTools
{
    private const string AnalysisScope = "saved_document_diagnostics";

    [McpServerTool(Name = "akbura_analyze_document", UseStructuredContent = true, OutputSchemaType = typeof(AnalysisToolResponse),
        ReadOnly = true, OpenWorld = false)]
    [Description("Return canonical Workspace diagnostics for one registered, saved .akbura or .akcss document.")]
    public async Task<CallToolResult> AnalyzeDocumentAsync(
        [Description("Project-relative or absolute path of a registered, saved .akbura or .akcss document.")]
        string documentPath,
        [Description("Use syntax for parsing or semantic for syntax and semantic checks.")]
        string analysisLevel = "semantic",
        [Range(1, 500)] int maxDiagnostics = 100,
        CancellationToken cancellationToken = default)
    {
        var level = analysisLevel switch
        {
            "syntax" => AkburaDiagnosticAnalysisLevel.Syntax,
            "semantic" => AkburaDiagnosticAnalysisLevel.Semantic,
            _ => (AkburaDiagnosticAnalysisLevel?)null,
        };
        if (level == null)
        {
            return AnalysisError(documentPath, "invalid_analysis_level", "Choose syntax or semantic.", analysisLevel);
        }
        if (maxDiagnostics is < 1 or > 500)
        {
            return AnalysisError(documentPath, "invalid_limit", "maxDiagnostics must be between 1 and 500.", analysisLevel);
        }
        if (string.IsNullOrWhiteSpace(documentPath))
        {
            return AnalysisError(documentPath, "unknown_document",
                "Supply the path of a registered .akbura or .akcss document.", analysisLevel);
        }

        var view = await session.CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (view.Snapshot is not { } snapshot)
        {
            return AnalysisError(documentPath, view.Status, view.Message!, analysisLevel);
        }
        if (SelectDocument(snapshot, documentPath, allowAkcss: true, out var error) is not { } context)
        {
            return AnalysisError(documentPath, "unknown_document", error!, analysisLevel);
        }

        var result = session.Workspace.LanguageServices.DocumentAnalysis.AnalyzeDocument(context, level.Value, IsLoadComplete, cancellationToken);
        var (issues, counts) = McpDiagnosticMapper.MapDiagnostics(result.Diagnostics,
            result.DocumentPath, "document", maxDiagnostics, cancellationToken);
        var response = new AnalysisToolResponse(result.IsComplete ? "completed" : "incomplete",
            result.DocumentPath, McpDiagnosticMapper.MapSnapshot(result.Snapshot),
            McpDiagnosticMapper.MapLevel(result.RequestedLevel), McpDiagnosticMapper.MapLevel(result.CompletedLevel),
            AnalysisScope, result.IsComplete, issues, counts, counts.Errors > 0, issues.Length < counts.Total,
            result.IncompleteReasons.ToArray(), GetLoadDiagnostics(), null);

        cancellationToken.ThrowIfCancellationRequested();
        return FormatResponse(response, isError: false, $"Document analysis is {response.Status}. Collected {counts.Total} diagnostics.");
    }

    private static CallToolResult AnalysisError(string? path, string status, string message, string? level) =>
        FormatResponse(new AnalysisToolResponse(status, path, null, level, null, AnalysisScope, false,
            [], new(0, 0, 0, 0, 0), false, false, [], [], new(status, message)), isError: true, message);
}
