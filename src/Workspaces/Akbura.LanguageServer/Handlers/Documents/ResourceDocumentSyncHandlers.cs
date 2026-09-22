using Microsoft.CodeAnalysis;

namespace Akbura.LanguageServer.Handlers.Documents;

// Resource buffers deliberately use private notifications instead of the
// ordinary textDocument sync route. They update completion inputs without
// becoming Akbura documents or acquiring Akbura diagnostics.
internal sealed class ResourceDocumentDidOpenHandler :
    AkburaLspHandler<DidOpenTextDocumentParams, object?>
{
    public override string Method => LspMethods.ResourceDocumentDidOpen;

    public override bool MutatesServerState => true;

    public override Uri? GetDocumentUri(DidOpenTextDocumentParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(DidOpenTextDocumentParams parameters, AkburaRequestContext context, CancellationToken cancellationToken)
    {
        var item = parameters.TextDocument;
        var uri = AkburaProtocolMapper.ParseUri(item.Uri);
        AkburaResourceDocumentSynchronization.ValidateUri(uri);

        if (context.ServerSnapshot.OpenResourceDocuments.ContainsKey(uri))
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidRequest,
                $"Resource document '{uri}' is already open.");
        }

        var text = SourceText.From(item.Text);
        var projects = AkburaResourceDocumentSynchronization
            .FindProjects(context.Solution, uri);
        var document = new AkburaOpenResourceDocument(
            uri,
            item.Version,
            text,
            projects);

        AkburaResourceDocumentSynchronization.ApplyOpenText(
            context.Services.Workspace,
            document,
            changes: null,
            cancellationToken);

        var next = context.ServerSnapshot
            .Next(context.Services.Workspace.CurrentSolution) with
            {
                OpenResourceDocuments = context.ServerSnapshot
                    .OpenResourceDocuments
                    .Add(uri, document),
            };

        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next));
    }
}

internal sealed class ResourceDocumentDidChangeHandler :
    AkburaLspHandler<DidChangeTextDocumentParams, object?>
{
    public override string Method => LspMethods.ResourceDocumentDidChange;

    public override bool MutatesServerState => true;

    public override Uri? GetDocumentUri(DidChangeTextDocumentParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(DidChangeTextDocumentParams parameters, AkburaRequestContext context, CancellationToken cancellationToken)
    {
        var uri = AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
        var current = AkburaResourceDocumentSynchronization
            .GetRequiredOpenDocument(context.ServerSnapshot, uri);
        var requestedVersion = parameters.TextDocument.Version;
        if (requestedVersion <= current.Version)
        {
            context.Services.Logger.Log(
                AkburaServerLogLevel.Warning,
                $"Ignored stale resource didChange version " +
                $"{requestedVersion} for '{uri}'; current version is " +
                $"{current.Version}.");
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

        var changes = newText
            .GetChangeRanges(oldText)
            .ToImmutableArray();
        var document = current with
        {
            Version = requestedVersion,
            Text = newText,
        };

        AkburaResourceDocumentSynchronization.ApplyOpenText(
            context.Services.Workspace,
            document,
            changes,
            cancellationToken);

        var next = context.ServerSnapshot
            .Next(context.Services.Workspace.CurrentSolution) with
            {
                OpenResourceDocuments = context.ServerSnapshot
                    .OpenResourceDocuments
                    .SetItem(uri, document),
            };

        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next));
    }
}

internal sealed class ResourceDocumentDidSaveHandler :
    AkburaLspHandler<DidSaveTextDocumentParams, object?>
{
    public override string Method => LspMethods.ResourceDocumentDidSave;

    public override bool MutatesServerState => true;

    public override Uri? GetDocumentUri(DidSaveTextDocumentParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(DidSaveTextDocumentParams parameters, AkburaRequestContext context, CancellationToken cancellationToken)
    {
        var uri = AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
        var current = AkburaResourceDocumentSynchronization
            .GetRequiredOpenDocument(context.ServerSnapshot, uri);
        var savedText = parameters.Text == null
            ? current.Text
            : SourceText.From(parameters.Text);
        var version = VersionStamp.Create();
        var projects = current.Projects
            .Select(project => project with
            {
                PersistedInput = project.PersistedInput.WithText(
                    savedText,
                    version),
            })
            .ToImmutableArray();
        var document = current with
        {
            Text = savedText,
            Projects = projects,
        };

        AkburaResourceDocumentSynchronization.ApplyOpenText(
            context.Services.Workspace,
            document,
            changes: null,
            cancellationToken);

        var next = context.ServerSnapshot
            .Next(context.Services.Workspace.CurrentSolution) with
            {
                OpenResourceDocuments = context.ServerSnapshot
                    .OpenResourceDocuments
                    .SetItem(uri, document),
            };

        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next));
    }
}

internal sealed class ResourceDocumentDidCloseHandler :
    AkburaLspHandler<DidCloseTextDocumentParams, object?>
{
    public override string Method => LspMethods.ResourceDocumentDidClose;

    public override bool MutatesServerState => true;

    public override Uri? GetDocumentUri(DidCloseTextDocumentParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(DidCloseTextDocumentParams parameters, AkburaRequestContext context, CancellationToken cancellationToken)
    {
        var uri = AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
        var document = AkburaResourceDocumentSynchronization
            .GetRequiredOpenDocument(context.ServerSnapshot, uri);
        var workspace = context.Services.Workspace;
        workspace.OpenOrChangeResourceDocuments(
            document.Projects
                .Select(static project =>
                    (project.ProjectId, project.PersistedInput))
                .ToImmutableArray(),
            changes: null,
            cancellationToken);

        var next = context.ServerSnapshot
            .Next(workspace.CurrentSolution) with
            {
                OpenResourceDocuments = context.ServerSnapshot
                    .OpenResourceDocuments
                    .Remove(uri),
            };

        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next));
    }
}

internal static class AkburaResourceDocumentSynchronization
{
    public static void ValidateUri(Uri uri)
    {
        if (!uri.IsFile ||
            !RoslynResourceDocumentLoader.IsAvaloniaResourceDocument(
                uri.LocalPath))
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidParams,
                $"Resource document '{uri}' must be a file URI ending " +
                "in '.axaml'.");
        }
    }

    public static AkburaOpenResourceDocument GetRequiredOpenDocument(AkburaServerSnapshot snapshot, Uri uri)
    {
        ValidateUri(uri);
        if (snapshot.OpenResourceDocuments.TryGetValue(
                uri,
                out var document))
        {
            return document;
        }

        throw new AkburaProtocolException(
            LspErrorCodes.InvalidParams,
            $"Resource document '{uri}' is not open.");
    }

    public static ImmutableArray<AkburaOpenResourceDocumentProject> FindProjects(AkburaSolutionSnapshot solution, Uri uri)
    {
        var projects = ImmutableArray.CreateBuilder<
            AkburaOpenResourceDocumentProject>();
        foreach (var project in solution.Projects.Values)
        {
            foreach (var resourceDocument in project.ResourceDocuments.Values)
            {
                if (!DocumentUri.Equals(resourceDocument.Uri, uri))
                {
                    continue;
                }

                projects.Add(
                    new AkburaOpenResourceDocumentProject(
                        project.Id,
                        resourceDocument.Input));
                break;
            }
        }

        return projects.ToImmutable();
    }

    public static ImmutableArray<AkburaOpenResourceDocumentProject> RebindProjects(AkburaSolutionSnapshot solution, AkburaOpenResourceDocument document, ISet<AkburaProjectId> refreshedProjects)
    {
        var previous = document.Projects.ToDictionary(
            static project => project.ProjectId);
        var projects = FindProjects(solution, document.Uri);
        for (var index = 0; index < projects.Length; index++)
        {
            var project = projects[index];
            if (!refreshedProjects.Contains(project.ProjectId) &&
                previous.TryGetValue(
                    project.ProjectId,
                    out var persisted))
            {
                projects = projects.SetItem(index, persisted);
            }
        }

        return projects;
    }

    public static void ApplyOpenText(AkburaWorkspace workspace, AkburaOpenResourceDocument document, IReadOnlyList<TextChangeRange>? changes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = VersionStamp.Create();
        var updates = ImmutableArray.CreateBuilder<(
            AkburaProjectId ProjectId,
            ResourceDocumentInput Input)>(document.Projects.Length);
        foreach (var project in document.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!workspace.CurrentSolution.TryGetProject(
                    project.ProjectId,
                    out _))
            {
                continue;
            }

            updates.Add(
                (project.ProjectId,
                 project.PersistedInput.WithText(
                     document.Text,
                     version)));
        }

        workspace.OpenOrChangeResourceDocuments(
            updates.ToImmutable(),
            changes,
            cancellationToken);
    }
}
