namespace Akbura.LanguageServer.Handlers.Documents;

internal sealed class DidOpenHandler :
    AkburaLspHandler<DidOpenTextDocumentParams, object?>
{
    public override string Method => LspMethods.DidOpen;

    public override bool MutatesServerState => true;

    public override Uri? GetDocumentUri(
        DidOpenTextDocumentParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(
        DidOpenTextDocumentParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var item = parameters.TextDocument;
        var uri = AkburaProtocolMapper.ParseUri(item.Uri);
        if (context.ServerSnapshot.OpenDocuments.ContainsKey(uri))
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidRequest,
                $"Document '{uri}' is already open.");
        }

        var text = SourceText.From(item.Text);
        var syntactic = AkburaSyntacticDocument.Parse(
            text,
            GetFilePath(uri, item.LanguageId),
            cancellationToken);
        AkburaProjectId? projectId = null;
        AkburaDocumentId? documentId = null;
        SourceText? projectText = null;
        var solution = context.Solution;

        if (solution.TryGetDocument(uri, out var existingDocument))
        {
            projectId = existingDocument.ProjectId;
            documentId = existingDocument.Id;
            projectText = existingDocument.Text;
            var semantic = context.Services.Workspace
                .OpenOrChangeDocumentContext(
                    existingDocument.ProjectId,
                    uri,
                    text,
                    changes: null,
                    cancellationToken);
            documentId = semantic.Document.Id;
            solution = context.Services.Workspace.CurrentSolution;
        }

        var document = new AkburaOpenDocument(
            uri,
            item.LanguageId,
            item.Version,
            text,
            syntactic,
            projectId,
            documentId,
            projectText);
        var next = context.ServerSnapshot
            .Next(solution) with
            {
                OpenDocuments =
                    context.ServerSnapshot.OpenDocuments.Add(
                        uri,
                        document),
            };

        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next,
                afterCommit: async token =>
                {
                    await context.Services.Diagnostics
                        .PublishSyntacticAsync(document, token)
                        .ConfigureAwait(false);
                    if (documentId != null)
                    {
                        await context.Services.Diagnostics
                            .PublishSemanticAsync(uri, token)
                            .ConfigureAwait(false);
                    }
                }));
    }

    internal static string GetFilePath(
        Uri uri,
        string? languageId)
    {
        var path = uri.IsFile
            ? uri.LocalPath
            : Uri.UnescapeDataString(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(path)))
        {
            path += string.Equals(
                    languageId,
                    "akcss",
                    StringComparison.OrdinalIgnoreCase)
                ? ".akcss"
                : ".akbura";
        }

        return path;
    }
}

internal sealed class DidChangeHandler :
    AkburaLspHandler<DidChangeTextDocumentParams, object?>
{
    public override string Method => LspMethods.DidChange;

    public override bool MutatesServerState => true;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        DidChangeTextDocumentParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(
        DidChangeTextDocumentParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var current = context.OpenDocument!;
        var requestedVersion = parameters.TextDocument.Version;
        if (requestedVersion <= current.Version)
        {
            context.Services.Logger.Log(
                AkburaServerLogLevel.Warning,
                $"Ignored stale didChange version {requestedVersion} for " +
                $"'{current.Uri}'; current version is {current.Version}.");
            return Task.FromResult(
                new AkburaLspHandlerResult<object?>(null));
        }

        var oldText = current.Text;
        var newText = oldText;
        foreach (var change in parameters.ContentChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextSpan span;
            if (change.Range == null)
            {
                span = new TextSpan(0, newText.Length);
            }
            else
            {
                span = context.Services.PositionConverter.ToTextSpan(
                    newText,
                    change.Range);
                if (change.RangeLength is { } rangeLength &&
                    rangeLength != span.Length)
                {
                    throw new AkburaProtocolException(
                        LspErrorCodes.InvalidParams,
                        $"Change rangeLength {rangeLength} does not match " +
                        $"the UTF-16 span length {span.Length}.");
                }
            }

            newText = newText.WithChanges(
                new TextChange(span, change.Text));
        }

        var changeRanges = newText
            .GetChangeRanges(oldText)
            .ToImmutableArray();
        var syntactic = current.SyntacticDocument.WithText(
            newText,
            changeRanges,
            cancellationToken);
        AkburaDocumentId? documentId = current.DocumentId;
        var solution = context.Solution;

        if (current.ProjectId is { } projectId)
        {
            var semantic = context.Services.Workspace
                .OpenOrChangeDocumentContext(
                    projectId,
                    current.Uri,
                    newText,
                    changeRanges,
                    cancellationToken);
            documentId = semantic.Document.Id;
            solution = context.Services.Workspace.CurrentSolution;
        }

        var document = current with
        {
            Version = requestedVersion,
            Text = newText,
            SyntacticDocument = syntactic,
            DocumentId = documentId,
        };
        var next = context.ServerSnapshot
            .Next(solution) with
            {
                OpenDocuments =
                    context.ServerSnapshot.OpenDocuments.SetItem(
                        current.Uri,
                        document),
            };

        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next,
                afterCommit: async token =>
                {
                    await context.Services.Diagnostics
                        .PublishSyntacticAsync(document, token)
                        .ConfigureAwait(false);
                    if (documentId != null)
                    {
                        await context.Services.Diagnostics
                            .PublishSemanticAsync(
                                current.Uri,
                                token)
                            .ConfigureAwait(false);
                    }
                }));
    }
}

internal sealed class DidCloseHandler :
    AkburaLspHandler<DidCloseTextDocumentParams, object?>
{
    public override string Method => LspMethods.DidClose;

    public override bool MutatesServerState => true;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        DidCloseTextDocumentParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(
        DidCloseTextDocumentParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var document = context.OpenDocument!;
        if (document.DocumentId is { } documentId)
        {
            if (document.ProjectId is { } projectId &&
                document.ProjectText is { } projectText)
            {
                var restored = context.Services.Workspace
                    .OpenOrChangeDocumentContext(
                        projectId,
                        document.Uri,
                        projectText,
                        changes: null,
                        cancellationToken);
                context.Services.Workspace.CloseDocument(
                    restored.Document.Id);
            }
            else
            {
                context.Services.Workspace.RemoveDocument(documentId);
            }
        }

        var next = context.ServerSnapshot
            .Next(context.Services.Workspace.CurrentSolution) with
            {
                OpenDocuments =
                    context.ServerSnapshot.OpenDocuments.Remove(
                        document.Uri),
            };
        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next,
                afterCommit: async token =>
                {
                    context.Services.SemanticTokens.Remove(document.Uri);
                    await context.Services.Diagnostics.ClearAsync(
                            document.Uri,
                            token)
                        .ConfigureAwait(false);
                }));
    }
}

internal sealed class DidSaveHandler :
    AkburaLspHandler<DidSaveTextDocumentParams, object?>
{
    public override string Method => LspMethods.DidSave;

    public override bool MutatesServerState => true;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        DidSaveTextDocumentParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(
        DidSaveTextDocumentParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var current = context.OpenDocument!;
        var savedText = parameters.Text == null
            ? current.Text
            : SourceText.From(parameters.Text);
        var document = current with
        {
            ProjectText = savedText,
        };
        var next = context.ServerSnapshot
            .Next(context.Solution) with
            {
                OpenDocuments =
                    context.ServerSnapshot.OpenDocuments.SetItem(
                        current.Uri,
                        document),
            };
        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next,
                afterCommit: token =>
                    context.Services.Diagnostics.PublishSemanticAsync(
                        current.Uri,
                        token)));
    }
}

internal sealed class DidChangeWatchedFilesHandler :
    AkburaLspHandler<DidChangeWatchedFilesParams, object?>
{
    public override string Method =>
        LspMethods.DidChangeWatchedFiles;

    public override bool MutatesServerState => true;

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(
        DidChangeWatchedFilesParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var next = context.ServerSnapshot.Next(context.Solution);
        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next,
                afterCommit: token =>
                    context.Services.Projects
                        .HandleWatchedFilesAsync(
                            parameters,
                            token)));
    }
}
