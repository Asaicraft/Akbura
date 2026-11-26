using Akbura.Workspaces.Layout;
using Akbura.Workspaces.Utilities;

namespace Akbura.Workspaces;

/// <summary>
/// Provides the semantic and editor services associated with a Workspace.
/// </summary>
public interface IAkburaLanguageServices
{
    IAkburaTypingService Typing { get; }

    IAkburaClassificationService Classification { get; }

    IAkburaDiagnosticService Diagnostics { get; }

    IAkburaDocumentAnalysisService DocumentAnalysis { get; }

    IAkburaDefinitionService Definition { get; }

    IAkburaCompletionService Completion { get; }

    IAkburaUtilityCatalogService UtilityCatalog { get; }

    IAkburaLayoutRecipeService LayoutRecipes { get; }

    IAkburaProjectedCSharpService ProjectedCSharp { get; }

    IAkburaQuickInfoService QuickInfo { get; }

    IAkburaCodeActionService CodeActions { get; }

    IAkburaDocumentSymbolService DocumentSymbols { get; }

    IAkburaFoldingRangeService FoldingRanges { get; }

    IAkburaFindReferencesService References { get; }

    IAkburaDocumentHighlightService DocumentHighlights { get; }

    IAkburaRenameService Rename { get; }

    IAkburaWorkspaceSymbolService WorkspaceSymbols { get; }

    IAkburaSignatureHelpService SignatureHelp { get; }

    IAkburaFormattingService Formatting { get; }
}
