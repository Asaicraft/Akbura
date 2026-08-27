namespace Akbura.Workspaces;

public interface IAkburaLanguageServices
{
    IAkburaTypingService Typing { get; }

    IAkburaClassificationService Classification { get; }

    IAkburaDiagnosticService Diagnostics { get; }

    IAkburaDefinitionService Definition { get; }

    IAkburaCompletionService Completion { get; }

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