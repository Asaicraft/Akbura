namespace Akbura.Workspaces.Diagnostics;

/// <summary>
/// Analyzes a document snapshot and reports canonical diagnostics for the completed stages.
/// </summary>
public interface IAkburaDocumentAnalysisService
{
    /// <summary>Analyzes the captured document and reports which stages completed.</summary>
    AkburaDocumentAnalysisResult AnalyzeDocument(
        AkburaDocumentContext context,
        AkburaDiagnosticAnalysisLevel level = AkburaDiagnosticAnalysisLevel.Semantic,
        bool isProjectComplete = true,
        CancellationToken cancellationToken = default);
}
