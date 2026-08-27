using System.Text.Json;

namespace Akbura.LanguageServer.Handlers.Completion;

internal sealed class CompletionHandler :
    AkburaLspHandler<CompletionParams, CompletionList>
{
    public override string Method => LspMethods.Completion;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        CompletionParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override async Task<AkburaLspHandlerResult<CompletionList>>
        HandleAsync(
            CompletionParams parameters,
            AkburaRequestContext context,
            CancellationToken cancellationToken)
    {
        var document = context.OpenDocument!;
        var position = context.Services.PositionConverter.ToOffset(
            document.Text,
            parameters.Position);
        var services = context.Services.Workspace.LanguageServices;
        var native = services.Completion.GetCompletions(
            document.SyntacticDocument,
            context.SemanticDocument,
            position,
            cancellationToken);
        var triggerKind = parameters.Context?.TriggerKind ?? 1;
        var triggerText = parameters.Context?.TriggerCharacter;
        var projected = await services.ProjectedCSharp
            .GetCompletionsAsync(
                document.SyntacticDocument,
                context.SemanticDocument,
                position,
                new AkburaProjectedCompletionTrigger(
                    IsExplicit: triggerKind == 1,
                    IsIncomplete: triggerKind == 3,
                    Character: string.IsNullOrEmpty(triggerText)
                        ? '\0'
                        : triggerText![0]),
                cancellationToken)
            .ConfigureAwait(false);

        context.Services.Logger.Log(
            AkburaServerLogLevel.Trace,
            $"Completion: uri='{document.Uri}', " +
            $"version={document.Version}, " +
            $"semantic={context.SemanticDocument != null}, " +
            $"native={native.Items.Length}, " +
            $"projected={projected?.Items.Length ?? 0}, " +
            $"position={position}.");

        var capacity = native.Items.Length +
            (projected?.Items.Length ?? 0);
        var items = new List<Protocol.CompletionItem>(capacity);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        if (projected is { } projectedResult)
        {
            foreach (var item in projectedResult.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (identities.Add("csharp\0" + item.ResolveKey))
                {
                    items.Add(MapProjectedItem(
                        item,
                        document,
                        position,
                        context));
                }
            }
        }

        foreach (var sourceItem in native.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!identities.Add("akbura\0" + sourceItem.ResolveKey))
            {
                continue;
            }

            var change = services.Completion.GetCompletionChange(
                document.SyntacticDocument,
                context.SemanticDocument,
                position,
                sourceItem,
                cancellationToken);
            items.Add(MapNativeItem(
                sourceItem,
                change,
                native.ApplicableSpan,
                document,
                position,
                context));
        }

        return new AkburaLspHandlerResult<CompletionList>(
            new CompletionList
            {
                IsIncomplete = native.IsIncomplete ||
                    projected?.IsIncomplete == true,
                Items = items.ToArray(),
            });
    }

    private static Protocol.CompletionItem MapProjectedItem(
        AkburaProjectedCompletionItem source,
        AkburaOpenDocument document,
        int position,
        AkburaRequestContext context)
    {
        return new Protocol.CompletionItem
        {
            Label = source.DisplayText,
            Kind = AkburaProtocolMapper.ToCompletionItemKind(source.Kind),
            Detail = source.Detail,
            SortText = source.SortText,
            FilterText = source.FilterText,
            InsertTextFormat = 1,
            TextEdit = new TextEdit
            {
                Range = context.Services.PositionConverter.ToRange(
                    document.Text,
                    source.SourceSpan),
                NewText = source.InsertText,
            },
            Data = CreateResolveData(
                document,
                position,
                source.ResolveKey,
                "csharp",
                source.SourceSpan),
        };
    }

    private static Protocol.CompletionItem MapNativeItem(
        AkburaCompletionItem source,
        AkburaCompletionChange change,
        TextSpan applicableSpan,
        AkburaOpenDocument document,
        int position,
        AkburaRequestContext context)
    {
        var item = new Protocol.CompletionItem
        {
            Label = source.DisplayText,
            Kind = AkburaProtocolMapper.ToCompletionItemKind(source.Kind),
            Detail = string.IsNullOrEmpty(source.Suffix)
                ? null
                : source.Suffix,
            SortText = source.SortText,
            FilterText = source.FilterText,
            Data = CreateResolveData(
                document,
                position,
                source.ResolveKey,
                "akbura",
                applicableSpan),
        };
        ApplyCompletionChange(
            item,
            change,
            applicableSpan,
            source.InsertText,
            document,
            context);
        return item;
    }

    internal static void ApplyCompletionChange(
        Protocol.CompletionItem item,
        AkburaCompletionChange change,
        TextSpan applicableSpan,
        string fallbackText,
        AkburaOpenDocument document,
        AkburaRequestContext context)
    {
        var changes = change.Changes;
        var mainIndex = FindMainChange(changes, applicableSpan);
        var mainChange = changes.IsDefaultOrEmpty
            ? new TextChange(applicableSpan, fallbackText)
            : changes[mainIndex];
        var newText = mainChange.NewText ?? string.Empty;
        var insertTextFormat = 1;

        if (context.ClientCapabilities.SupportsSnippets)
        {
            var deltaBeforeMain = 0;
            for (var index = 0; index < changes.Length; index++)
            {
                if (index == mainIndex)
                {
                    continue;
                }

                var candidate = changes[index];
                if (candidate.Span.End <= mainChange.Span.Start)
                {
                    deltaBeforeMain +=
                        (candidate.NewText?.Length ?? 0) -
                        candidate.Span.Length;
                }
            }

            var finalMainStart = mainChange.Span.Start + deltaBeforeMain;
            var caretOffset = Math.Clamp(
                change.NewPosition - finalMainStart,
                0,
                newText.Length);
            if (caretOffset != newText.Length)
            {
                newText = AkburaProtocolMapper.EscapeSnippet(
                        newText[..caretOffset]) +
                    "$0" +
                    AkburaProtocolMapper.EscapeSnippet(
                        newText[caretOffset..]);
                insertTextFormat = 2;
            }
        }

        var additional = new List<TextEdit>();
        for (var index = 0; index < changes.Length; index++)
        {
            if (index == mainIndex)
            {
                continue;
            }

            var candidate = changes[index];
            additional.Add(new TextEdit
            {
                Range = context.Services.PositionConverter.ToRange(
                    document.Text,
                    candidate.Span),
                NewText = candidate.NewText ?? string.Empty,
            });
        }

        item.InsertTextFormat = insertTextFormat;
        item.TextEdit = new TextEdit
        {
            Range = context.Services.PositionConverter.ToRange(
                document.Text,
                mainChange.Span),
            NewText = newText,
        };
        item.AdditionalTextEdits = additional.Count == 0
            ? null
            : additional.ToArray();
    }

    private static JsonElement CreateResolveData(
        AkburaOpenDocument document,
        int position,
        string resolveKey,
        string provider,
        TextSpan sourceSpan)
    {
        return JsonSerializer.SerializeToElement(
            new AkburaCompletionResolveData
            {
                Uri = document.Uri.AbsoluteUri,
                Version = document.Version,
                Position = position,
                ResolveKey = resolveKey,
                Provider = provider,
                SourceStart = sourceSpan.Start,
                SourceLength = sourceSpan.Length,
            });
    }

    private static int FindMainChange(
        ImmutableArray<TextChange> changes,
        TextSpan applicableSpan)
    {
        if (changes.IsDefaultOrEmpty)
        {
            return 0;
        }

        for (var index = 0; index < changes.Length; index++)
        {
            if (changes[index].Span == applicableSpan)
            {
                return index;
            }
        }

        for (var index = 0; index < changes.Length; index++)
        {
            if (changes[index].Span.Contains(applicableSpan.Start))
            {
                return index;
            }
        }

        return changes.Length - 1;
    }
}

internal sealed class CompletionResolveHandler :
    AkburaLspHandler<Protocol.CompletionItem, Protocol.CompletionItem>
{
    public override string Method => LspMethods.CompletionResolve;

    public override async Task<AkburaLspHandlerResult<Protocol.CompletionItem>>
        HandleAsync(
            Protocol.CompletionItem parameters,
            AkburaRequestContext context,
            CancellationToken cancellationToken)
    {
        if (parameters.Data is not { } dataElement)
        {
            return new AkburaLspHandlerResult<Protocol.CompletionItem>(
                parameters);
        }

        AkburaCompletionResolveData? data;
        try
        {
            data = dataElement.Deserialize<AkburaCompletionResolveData>();
        }
        catch (JsonException exception)
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidParams,
                "Completion resolve data is invalid.",
                exception);
        }

        if (data == null)
        {
            return new AkburaLspHandlerResult<Protocol.CompletionItem>(
                parameters);
        }

        var uri = AkburaProtocolMapper.ParseUri(data.Uri);
        var snapshot = context.ServerSnapshot;
        if (!snapshot.OpenDocuments.TryGetValue(uri, out var document) ||
            document.Version != data.Version)
        {
            return new AkburaLspHandlerResult<Protocol.CompletionItem>(
                parameters);
        }

        snapshot.Solution.TryGetDocumentContext(
            uri,
            out var semanticContext);
        var services = context.Services.Workspace.LanguageServices;
        if (string.Equals(
                data.Provider,
                "csharp",
                StringComparison.Ordinal))
        {
            var resolution = await services.ProjectedCSharp
                .ResolveCompletionAsync(
                    document.SyntacticDocument,
                    semanticContext,
                    data.Position,
                    data.ResolveKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (resolution != null)
            {
                parameters.Detail = resolution.Detail ?? parameters.Detail;
                if (!string.IsNullOrWhiteSpace(resolution.Documentation))
                {
                    parameters.Documentation = new MarkupContent
                    {
                        Kind = "markdown",
                        Value = resolution.Documentation,
                    };
                }

                CompletionHandler.ApplyCompletionChange(
                    parameters,
                    resolution.Change,
                    new TextSpan(data.SourceStart, data.SourceLength),
                    parameters.TextEdit?.NewText ?? parameters.Label,
                    document,
                    context);
            }

            return new AkburaLspHandlerResult<Protocol.CompletionItem>(
                parameters);
        }

        var completion = services.Completion.GetCompletions(
            document.SyntacticDocument,
            semanticContext,
            data.Position,
            cancellationToken);
        var item = completion.Items.FirstOrDefault(candidate =>
            string.Equals(
                candidate.ResolveKey,
                data.ResolveKey,
                StringComparison.Ordinal));
        if (item != null)
        {
            var description = await Task.Run(
                    () => item.Description,
                    cancellationToken)
                .ConfigureAwait(false);
            parameters.Documentation = new MarkupContent
            {
                Kind = "markdown",
                Value = description,
            };
        }

        return new AkburaLspHandlerResult<Protocol.CompletionItem>(
            parameters);
    }
}
