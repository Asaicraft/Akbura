using Akbura.Workspaces.Formatting;
using Akbura.Workspaces.Symbols;

namespace Akbura.LanguageServer.Handlers.LanguageFeatures;

internal sealed class DocumentSymbolHandler :
    AkburaLspHandler<DocumentSymbolParams, DocumentSymbol[]>
{
    public override string Method => LspMethods.DocumentSymbol;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(DocumentSymbolParams parameters) =>
        AkburaProtocolMapper.ParseUri(parameters.TextDocument.Uri);

    public override Task<AkburaLspHandlerResult<DocumentSymbol[]>> HandleAsync(
        DocumentSymbolParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var symbols = context.Services.Workspace.LanguageServices
            .DocumentSymbols.GetSymbols(
                context.SyntacticDocument!,
                cancellationToken);
        var result = new DocumentSymbol[symbols.Length];
        for (var index = 0; index < symbols.Length; index++)
        {
            result[index] = Map(
                symbols[index],
                context.OpenDocument!.Text,
                context);
        }

        return Task.FromResult(
            new AkburaLspHandlerResult<DocumentSymbol[]>(result));
    }

    private static DocumentSymbol Map(
        AkburaDocumentSymbol symbol,
        SourceText text,
        AkburaRequestContext context)
    {
        var children = symbol.Children.IsDefaultOrEmpty
            ? null
            : symbol.Children.Select(child =>
                    Map(child, text, context))
                .ToArray();
        return new DocumentSymbol
        {
            Name = symbol.Name,
            Detail = symbol.Detail,
            Kind = MapSymbolKind(symbol.Kind),
            Range = context.Services.PositionConverter.ToRange(
                text,
                symbol.Span),
            SelectionRange = context.Services.PositionConverter.ToRange(
                text,
                symbol.SelectionSpan),
            Children = children,
        };
    }

    internal static int MapSymbolKind(AkburaWorkspaceSymbolKind kind) =>
        kind switch
        {
            AkburaWorkspaceSymbolKind.File => 1,
            AkburaWorkspaceSymbolKind.Module => 2,
            AkburaWorkspaceSymbolKind.Namespace => 3,
            AkburaWorkspaceSymbolKind.Class => 5,
            AkburaWorkspaceSymbolKind.Method => 6,
            AkburaWorkspaceSymbolKind.Property => 7,
            AkburaWorkspaceSymbolKind.Field => 8,
            AkburaWorkspaceSymbolKind.Interface => 11,
            AkburaWorkspaceSymbolKind.Function => 12,
            AkburaWorkspaceSymbolKind.Variable => 13,
            AkburaWorkspaceSymbolKind.Object => 19,
            AkburaWorkspaceSymbolKind.Event => 24,
            AkburaWorkspaceSymbolKind.Parameter => 26,
            _ => 13,
        };
}

internal sealed class WorkspaceSymbolHandler :
    AkburaLspHandler<WorkspaceSymbolParams, SymbolInformation[]>
{
    public override string Method => LspMethods.WorkspaceSymbol;

    public override Task<AkburaLspHandlerResult<SymbolInformation[]>>
        HandleAsync(
            WorkspaceSymbolParams parameters,
            AkburaRequestContext context,
            CancellationToken cancellationToken)
    {
        var symbols = context.Services.Workspace.LanguageServices
            .WorkspaceSymbols.Search(
                context.Solution,
                parameters.Query,
                cancellationToken);
        var result = new List<SymbolInformation>(symbols.Length);
        foreach (var symbol in symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.Solution.TryGetDocument(
                    symbol.Uri,
                    out var document))
            {
                continue;
            }

            result.Add(new SymbolInformation
            {
                Name = symbol.Name,
                Kind = DocumentSymbolHandler.MapSymbolKind(symbol.Kind),
                ContainerName = symbol.ContainerName,
                Location = new Location
                {
                    Uri = symbol.Uri.AbsoluteUri,
                    Range = context.Services.PositionConverter.ToRange(
                        document.Text,
                        symbol.Span),
                },
            });
        }

        return Task.FromResult(
            new AkburaLspHandlerResult<SymbolInformation[]>(
                result.ToArray()));
    }
}

internal sealed class FoldingRangeHandler :
    AkburaLspHandler<FoldingRangeParams, FoldingRange[]>
{
    public override string Method => LspMethods.FoldingRange;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(FoldingRangeParams parameters) =>
        AkburaProtocolMapper.ParseUri(parameters.TextDocument.Uri);

    public override Task<AkburaLspHandlerResult<FoldingRange[]>> HandleAsync(
        FoldingRangeParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var document = context.OpenDocument!;
        var regions = document.SyntacticDocument.OutliningRegions;
        var result = new List<FoldingRange>(regions.Length);
        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var range = context.Services.PositionConverter.ToRange(
                document.Text,
                region.Span);
            if (range.Start.Line >= range.End.Line)
            {
                continue;
            }

            result.Add(new FoldingRange
            {
                StartLine = range.Start.Line,
                StartCharacter = range.Start.Character,
                EndLine = range.End.Line,
                EndCharacter = range.End.Character,
                Kind = "region",
                CollapsedText = region.CollapsedText,
            });
        }

        return Task.FromResult(
            new AkburaLspHandlerResult<FoldingRange[]>(
                result.ToArray()));
    }
}

internal sealed class DocumentHighlightHandler :
    AkburaLspHandler<DocumentHighlightParams, DocumentHighlight[]>
{
    public override string Method => LspMethods.DocumentHighlight;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        DocumentHighlightParams parameters) =>
        AkburaProtocolMapper.ParseUri(parameters.TextDocument.Uri);

    public override Task<AkburaLspHandlerResult<DocumentHighlight[]>>
        HandleAsync(
            DocumentHighlightParams parameters,
            AkburaRequestContext context,
            CancellationToken cancellationToken)
    {
        if (context.SemanticDocument == null)
        {
            return Task.FromResult(
                new AkburaLspHandlerResult<DocumentHighlight[]>([]));
        }

        var position = context.Services.PositionConverter.ToOffset(
            context.OpenDocument!.Text,
            parameters.Position);
        var highlights = context.Services.Workspace.LanguageServices
            .DocumentHighlights.GetHighlights(
                context.SemanticDocument,
                position,
                cancellationToken);
        var result = new DocumentHighlight[highlights.Length];
        for (var index = 0; index < highlights.Length; index++)
        {
            result[index] = new DocumentHighlight
            {
                Range = context.Services.PositionConverter.ToRange(
                    context.OpenDocument.Text,
                    highlights[index].Span),
                Kind = highlights[index].IsWrite ? 3 : 2,
            };
        }

        return Task.FromResult(
            new AkburaLspHandlerResult<DocumentHighlight[]>(result));
    }
}

internal sealed class ReferencesHandler :
    AkburaLspHandler<ReferenceParams, Location[]>
{
    public override string Method => LspMethods.References;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(ReferenceParams parameters) =>
        AkburaProtocolMapper.ParseUri(parameters.TextDocument.Uri);

    public override Task<AkburaLspHandlerResult<Location[]>> HandleAsync(
        ReferenceParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.SemanticDocument == null)
        {
            return Task.FromResult(
                new AkburaLspHandlerResult<Location[]>([]));
        }

        var position = context.Services.PositionConverter.ToOffset(
            context.OpenDocument!.Text,
            parameters.Position);
        var references = context.Services.Workspace.LanguageServices
            .References.FindReferences(
                context.SemanticDocument,
                position,
                parameters.Context.IncludeDeclaration,
                cancellationToken);
        var result = new List<Location>(references.Locations.Length);
        foreach (var reference in references.Locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetText(context, reference.Uri, out var text))
            {
                continue;
            }

            result.Add(new Location
            {
                Uri = reference.Uri.AbsoluteUri,
                Range = context.Services.PositionConverter.ToRange(
                    text,
                    reference.Span),
            });
        }

        return Task.FromResult(
            new AkburaLspHandlerResult<Location[]>(result.ToArray()));
    }

    internal static bool TryGetText(
        AkburaRequestContext context,
        Uri uri,
        out SourceText text)
    {
        if (context.ServerSnapshot.OpenDocuments.TryGetValue(
                uri,
                out var open))
        {
            text = open.Text;
            return true;
        }

        if (context.Solution.TryGetDocument(uri, out var document))
        {
            text = document.Text;
            return true;
        }

        text = null!;
        return false;
    }
}

internal sealed class PrepareRenameHandler :
    AkburaLspHandler<PrepareRenameParams, PrepareRenameResult?>
{
    public override string Method => LspMethods.PrepareRename;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(PrepareRenameParams parameters) =>
        AkburaProtocolMapper.ParseUri(parameters.TextDocument.Uri);

    public override Task<AkburaLspHandlerResult<PrepareRenameResult?>>
        HandleAsync(
            PrepareRenameParams parameters,
            AkburaRequestContext context,
            CancellationToken cancellationToken)
    {
        if (context.SemanticDocument == null)
        {
            return Task.FromResult(
                new AkburaLspHandlerResult<PrepareRenameResult?>(null));
        }

        var position = context.Services.PositionConverter.ToOffset(
            context.OpenDocument!.Text,
            parameters.Position);
        var info = context.Services.Workspace.LanguageServices.Rename
            .GetRenameInfo(
                context.SemanticDocument,
                position,
                cancellationToken);
        var result = info.CanRename
            ? new PrepareRenameResult
            {
                Range = context.Services.PositionConverter.ToRange(
                    context.OpenDocument.Text,
                    info.Span),
                Placeholder = info.Placeholder ?? string.Empty,
            }
            : null;
        return Task.FromResult(
            new AkburaLspHandlerResult<PrepareRenameResult?>(result));
    }
}

internal sealed class RenameHandler :
    AkburaLspHandler<RenameParams, WorkspaceEdit>
{
    public override string Method => LspMethods.Rename;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(RenameParams parameters) =>
        AkburaProtocolMapper.ParseUri(parameters.TextDocument.Uri);

    public override Task<AkburaLspHandlerResult<WorkspaceEdit>> HandleAsync(
        RenameParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.SemanticDocument == null)
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidRequest,
                "Semantic context is not available for rename.");
        }

        var position = context.Services.PositionConverter.ToOffset(
            context.OpenDocument!.Text,
            parameters.Position);
        var edit = context.Services.Workspace.LanguageServices.Rename
            .GetRenameChanges(
                context.SemanticDocument,
                position,
                parameters.NewName,
                cancellationToken);
        var documents = new List<AkburaWorkspaceEditDocument>(
            edit.Changes.Count);
        foreach (var pair in edit.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferencesHandler.TryGetText(
                    context,
                    pair.Key,
                    out var text))
            {
                continue;
            }

            int? version = context.ServerSnapshot.OpenDocuments
                .TryGetValue(pair.Key, out var openDocument)
                    ? openDocument.Version
                    : null;
            documents.Add(
                new AkburaWorkspaceEditDocument(
                    pair.Key,
                    text,
                    pair.Value,
                    version));
        }

        return Task.FromResult(
            new AkburaLspHandlerResult<WorkspaceEdit>(
                AkburaWorkspaceEditMapper.Create(
                    documents.ToImmutableArray(),
                    context.ClientCapabilities.SupportsDocumentChanges,
                    context.Services.PositionConverter)));
    }
}

internal sealed class SignatureHelpHandler :
    AkburaLspHandler<SignatureHelpParams, SignatureHelp?>
{
    public override string Method => LspMethods.SignatureHelp;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(SignatureHelpParams parameters) =>
        AkburaProtocolMapper.ParseUri(parameters.TextDocument.Uri);

    public override Task<AkburaLspHandlerResult<SignatureHelp?>> HandleAsync(
        SignatureHelpParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var position = context.Services.PositionConverter.ToOffset(
            context.OpenDocument!.Text,
            parameters.Position);
        var help = context.Services.Workspace.LanguageServices.SignatureHelp
            .GetSignatureHelp(
                context.SyntacticDocument!,
                context.SemanticDocument,
                position,
                cancellationToken);
        if (help == null)
        {
            return Task.FromResult(
                new AkburaLspHandlerResult<SignatureHelp?>(null));
        }

        var signatures = new SignatureInformation[help.Signatures.Length];
        for (var index = 0; index < help.Signatures.Length; index++)
        {
            var source = help.Signatures[index];
            signatures[index] = new SignatureInformation
            {
                Label = source.Label,
                Documentation = ToMarkup(source.Documentation),
                Parameters = source.Parameters.Select(parameter =>
                        new ParameterInformation
                        {
                            Label = parameter.Label,
                            Documentation = ToMarkup(
                                parameter.Documentation),
                        })
                    .ToArray(),
            };
        }

        return Task.FromResult(
            new AkburaLspHandlerResult<SignatureHelp?>(
                new SignatureHelp
                {
                    Signatures = signatures,
                    ActiveSignature = help.ActiveSignature,
                    ActiveParameter = help.ActiveParameter,
                }));
    }

    private static MarkupContent? ToMarkup(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : new MarkupContent
            {
                Kind = "markdown",
                Value = value,
            };
}

internal sealed class DocumentFormattingHandler :
    AkburaLspHandler<DocumentFormattingParams, TextEdit[]>
{
    public override string Method => LspMethods.Formatting;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        DocumentFormattingParams parameters) =>
        AkburaProtocolMapper.ParseUri(parameters.TextDocument.Uri);

    public override Task<AkburaLspHandlerResult<TextEdit[]>> HandleAsync(
        DocumentFormattingParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var changes = context.Services.Workspace.LanguageServices.Formatting
            .FormatDocument(
                context.SyntacticDocument!,
                MapOptions(parameters.Options),
                cancellationToken);
        return Task.FromResult(MapChanges(changes, context));
    }

    internal static AkburaFormattingOptions MapOptions(
        FormattingOptions options) =>
        new(
            options.TabSize,
            options.InsertSpaces,
            options.TrimTrailingWhitespace ?? true,
            options.InsertFinalNewline ?? false,
            options.TrimFinalNewlines ?? false);

    internal static AkburaLspHandlerResult<TextEdit[]> MapChanges(
        ImmutableArray<TextChange> changes,
        AkburaRequestContext context) =>
        new(AkburaProtocolMapper.ToTextEdits(
            context.OpenDocument!.Text,
            changes,
            context.Services.PositionConverter));
}

internal sealed class DocumentRangeFormattingHandler :
    AkburaLspHandler<DocumentRangeFormattingParams, TextEdit[]>
{
    public override string Method => LspMethods.RangeFormatting;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        DocumentRangeFormattingParams parameters) =>
        AkburaProtocolMapper.ParseUri(parameters.TextDocument.Uri);

    public override Task<AkburaLspHandlerResult<TextEdit[]>> HandleAsync(
        DocumentRangeFormattingParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var range = context.Services.PositionConverter.ToTextSpan(
            context.OpenDocument!.Text,
            parameters.Range);
        var changes = context.Services.Workspace.LanguageServices.Formatting
            .FormatRange(
                context.SyntacticDocument!,
                range,
                DocumentFormattingHandler.MapOptions(parameters.Options),
                cancellationToken);
        return Task.FromResult(
            DocumentFormattingHandler.MapChanges(changes, context));
    }
}

internal sealed class DocumentOnTypeFormattingHandler :
    AkburaLspHandler<DocumentOnTypeFormattingParams, TextEdit[]>
{
    public override string Method => LspMethods.OnTypeFormatting;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        DocumentOnTypeFormattingParams parameters) =>
        AkburaProtocolMapper.ParseUri(parameters.TextDocument.Uri);

    public override Task<AkburaLspHandlerResult<TextEdit[]>> HandleAsync(
        DocumentOnTypeFormattingParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(parameters.Character))
        {
            return Task.FromResult(
                new AkburaLspHandlerResult<TextEdit[]>([]));
        }

        var position = context.Services.PositionConverter.ToOffset(
            context.OpenDocument!.Text,
            parameters.Position);
        var changes = context.Services.Workspace.LanguageServices.Formatting
            .FormatOnType(
                context.SyntacticDocument!,
                position,
                parameters.Character[0],
                DocumentFormattingHandler.MapOptions(parameters.Options),
                cancellationToken);
        return Task.FromResult(
            DocumentFormattingHandler.MapChanges(changes, context));
    }
}