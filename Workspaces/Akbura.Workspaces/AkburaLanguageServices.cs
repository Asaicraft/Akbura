namespace Akbura.Workspaces;

internal sealed class AkburaLanguageServices :  IAkburaLanguageServices
{
    public AkburaLanguageServices()
    {
        var referenceResolver = new AkcssReferenceResolver();

        Typing = new AkburaTypingService();

        Classification = new AkburaClassificationService(referenceResolver);

        Diagnostics = new AkburaDiagnosticService();

        Definition = new AkburaDefinitionService(referenceResolver);

        Completion = new AkburaCompletionService();

        ProjectedCSharp = new AkburaProjectedCSharpService();

        QuickInfo = new AkburaQuickInfoService(referenceResolver);

        CodeActions = new AkburaCodeActionService();

        DocumentSymbols = new AkburaDocumentSymbolService();

        FoldingRanges = new AkburaFoldingRangeService();

        var references = new AkburaFindReferencesService(referenceResolver);
        References = references;
        DocumentHighlights = references;
        Rename = new AkburaRenameService(references);

        WorkspaceSymbols = new AkburaWorkspaceSymbolService(
            DocumentSymbols);

        SignatureHelp = new AkburaSignatureHelpService();

        Formatting = new AkburaFormattingService();
    }

    public IAkburaTypingService Typing { get; }

    public IAkburaClassificationService Classification { get; }

    public IAkburaDiagnosticService Diagnostics { get; }

    public IAkburaDefinitionService Definition { get; }

    public IAkburaCompletionService Completion { get; }

    public IAkburaProjectedCSharpService ProjectedCSharp { get; }

    public IAkburaQuickInfoService QuickInfo { get; }

    public IAkburaCodeActionService CodeActions { get; }

    public IAkburaDocumentSymbolService DocumentSymbols { get; }

    public IAkburaFoldingRangeService FoldingRanges { get; }

    public IAkburaFindReferencesService References { get; }

    public IAkburaDocumentHighlightService DocumentHighlights { get; }

    public IAkburaRenameService Rename { get; }

    public IAkburaWorkspaceSymbolService WorkspaceSymbols { get; }

    public IAkburaSignatureHelpService SignatureHelp { get; }

    public IAkburaFormattingService Formatting { get; }
}
