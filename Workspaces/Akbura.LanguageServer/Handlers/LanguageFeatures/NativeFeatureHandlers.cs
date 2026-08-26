using System.Text;
using System.Text.Json;

namespace Akbura.LanguageServer.Handlers.LanguageFeatures;

internal sealed class HoverHandler :
    AkburaLspHandler<TextDocumentPositionParams, Hover?>
{
    public override string Method => LspMethods.Hover;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        TextDocumentPositionParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override async Task<AkburaLspHandlerResult<Hover?>> HandleAsync(
        TextDocumentPositionParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.SemanticDocument == null)
        {
            return new AkburaLspHandlerResult<Hover?>(null);
        }

        var document = context.OpenDocument!;
        var position = context.Services.PositionConverter.ToOffset(
            document.Text,
            parameters.Position);
        var services = context.Services.Workspace.LanguageServices;
        var info = services.QuickInfo.GetQuickInfo(
            context.SemanticDocument,
            position,
            cancellationToken);
        info ??= await services.ProjectedCSharp.GetQuickInfoAsync(
                document.SyntacticDocument,
                context.SemanticDocument,
                position,
                cancellationToken)
            .ConfigureAwait(false);
        if (info == null)
        {
            return new AkburaLspHandlerResult<Hover?>(null);
        }

        var markdown = new StringBuilder()
            .AppendLine("\u0060\u0060\u0060csharp")
            .AppendLine(info.Signature)
            .AppendLine("\u0060\u0060\u0060");
        foreach (var detail in info.Details)
        {
            if (!string.IsNullOrWhiteSpace(detail))
            {
                markdown.AppendLine()
                    .AppendLine(detail);
            }
        }

        return new AkburaLspHandlerResult<Hover?>(
            new Hover
            {
                Contents = new MarkupContent
                {
                    Kind = "markdown",
                    Value = markdown.ToString().TrimEnd(),
                },
                Range = context.Services.PositionConverter.ToRange(
                    document.Text,
                    info.SourceSpan),
            });
    }
}

internal sealed class DefinitionHandler :
    AkburaLspHandler<DefinitionParams, LocationLink[]?>
{
    private readonly AkburaMetadataSourceMaterializer _materializer =
        new();

    public override string Method => LspMethods.Definition;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        DefinitionParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override async Task<
        AkburaLspHandlerResult<LocationLink[]?>> HandleAsync(
        DefinitionParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.SemanticDocument == null)
        {
            return new AkburaLspHandlerResult<LocationLink[]?>(null);
        }

        var position = context.Services.PositionConverter.ToOffset(
            context.OpenDocument!.Text,
            parameters.Position);
        var definition = context.Services.Workspace
            .LanguageServices.Definition.GetDefinition(
                context.SemanticDocument,
                position,
                cancellationToken);
        if (definition == null)
        {
            return new AkburaLspHandlerResult<LocationLink[]?>(null);
        }

        var targetPath = await _materializer.MaterializeAsync(
                definition,
                cancellationToken)
            .ConfigureAwait(false);
        var targetRange = new Protocol.Range
        {
            Start = new Position
            {
                Line = definition.TargetLineSpan.Start.Line,
                Character =
                    definition.TargetLineSpan.Start.Character,
            },
            End = new Position
            {
                Line = definition.TargetLineSpan.End.Line,
                Character =
                    definition.TargetLineSpan.End.Character,
            },
        };
        return new AkburaLspHandlerResult<LocationLink[]?>(
        [
            new LocationLink
            {
                OriginSelectionRange =
                    context.Services.PositionConverter.ToRange(
                        context.OpenDocument.Text,
                        definition.SourceSpan),
                TargetUri = new Uri(
                    Path.GetFullPath(targetPath)).AbsoluteUri,
                TargetRange = targetRange,
                TargetSelectionRange = targetRange,
            },
        ]);
    }
}

internal sealed class CodeActionHandler :
    AkburaLspHandler<CodeActionParams, Protocol.CodeAction[]>
{
    public override string Method => LspMethods.CodeAction;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        CodeActionParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<
        AkburaLspHandlerResult<Protocol.CodeAction[]>> HandleAsync(
        CodeActionParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.SemanticDocument == null)
        {
            return Task.FromResult(
                new AkburaLspHandlerResult<
                    Protocol.CodeAction[]>([]));
        }

        var document = context.OpenDocument!;
        var span = context.Services.PositionConverter.ToTextSpan(
            document.Text,
            parameters.Range);
        var actions = context.Services.Workspace
            .LanguageServices.CodeActions.GetCodeActions(
                context.SemanticDocument,
                span,
                cancellationToken);
        var mapped = new Protocol.CodeAction[actions.Length];
        for (var index = 0; index < actions.Length; index++)
        {
            var action = actions[index];
            mapped[index] = new Protocol.CodeAction
            {
                Title = action.Title,
                Kind = "quickfix",
                Diagnostics = parameters.Context.Diagnostics.Length == 0
                    ? null
                    : parameters.Context.Diagnostics,
                IsPreferred = true,
                Edit = context.ClientCapabilities.SupportsCodeActionResolve
                    ? null
                    : CreateWorkspaceEdit(
                        action,
                        document,
                        context),
                Data = JsonSerializer.SerializeToElement(
                    new AkburaCodeActionResolveData
                    {
                        Uri = document.Uri.AbsoluteUri,
                        Version = document.Version,
                        Start = span.Start,
                        Length = span.Length,
                        EquivalenceKey = action.EquivalenceKey,
                    }),
            };
        }

        return Task.FromResult(
            new AkburaLspHandlerResult<
                Protocol.CodeAction[]>(mapped));
    }

    internal static WorkspaceEdit CreateWorkspaceEdit(
        AkburaCodeAction action,
        AkburaOpenDocument document,
        AkburaRequestContext context)
    {
        return AkburaWorkspaceEditMapper.Create(
            ImmutableArray.Create(
                new AkburaWorkspaceEditDocument(
                    document.Uri,
                    document.Text,
                    action.Changes,
                    document.Version)),
            context.ClientCapabilities.SupportsDocumentChanges,
            context.Services.PositionConverter);
    }
}

internal sealed class CodeActionResolveHandler :
    AkburaLspHandler<Protocol.CodeAction, Protocol.CodeAction>
{
    public override string Method => LspMethods.CodeActionResolve;

    public override Task<AkburaLspHandlerResult<Protocol.CodeAction>>
        HandleAsync(
            Protocol.CodeAction parameters,
            AkburaRequestContext context,
            CancellationToken cancellationToken)
    {
        if (parameters.Data is not { } dataElement)
        {
            return Task.FromResult(
                new AkburaLspHandlerResult<Protocol.CodeAction>(
                    parameters));
        }

        AkburaCodeActionResolveData? data;
        try
        {
            data = dataElement.Deserialize<AkburaCodeActionResolveData>();
        }
        catch (JsonException exception)
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidParams,
                "Code action resolve data is invalid.",
                exception);
        }

        if (data == null)
        {
            return Task.FromResult(
                new AkburaLspHandlerResult<Protocol.CodeAction>(
                    parameters));
        }

        var uri = AkburaProtocolMapper.ParseUri(data.Uri);
        if (!context.ServerSnapshot.OpenDocuments.TryGetValue(
                uri,
                out var document) ||
            document.Version != data.Version)
        {
            throw new AkburaProtocolException(
                LspErrorCodes.ContentModified,
                $"Document '{uri}' changed before the code action was resolved.");
        }

        if (data.Start < 0 ||
            data.Length < 0 ||
            data.Start > document.Text.Length ||
            data.Length > document.Text.Length - data.Start)
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidParams,
                "Code action resolve range is outside the document.");
        }

        AkburaDocumentContext? semanticContext;
        if (document.DocumentId is { } documentId)
        {
            context.Solution.TryGetDocumentContext(
                documentId,
                out semanticContext);
        }
        else
        {
            context.Solution.TryGetDocumentContext(
                uri,
                out semanticContext);
        }

        if (semanticContext == null)
        {
            return Task.FromResult(
                new AkburaLspHandlerResult<Protocol.CodeAction>(
                    parameters));
        }

        var actions = context.Services.Workspace.LanguageServices
            .CodeActions.GetCodeActions(
                semanticContext,
                new TextSpan(data.Start, data.Length),
                cancellationToken);
        var action = actions.FirstOrDefault(candidate =>
            string.Equals(
                candidate.EquivalenceKey,
                data.EquivalenceKey,
                StringComparison.Ordinal));
        if (action != null)
        {
            parameters.Edit = CodeActionHandler.CreateWorkspaceEdit(
                action,
                document,
                context);
        }

        return Task.FromResult(
            new AkburaLspHandlerResult<Protocol.CodeAction>(
                parameters));
    }
}