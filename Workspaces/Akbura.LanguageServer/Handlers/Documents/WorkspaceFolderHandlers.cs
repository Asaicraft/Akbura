namespace Akbura.LanguageServer.Handlers.Documents;

internal sealed class DidChangeWorkspaceFoldersHandler :
    AkburaLspHandler<DidChangeWorkspaceFoldersParams, object?>
{
    public override string Method => LspMethods.DidChangeWorkspaceFolders;

    public override bool MutatesServerState => true;

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(
        DidChangeWorkspaceFoldersParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var folders = context.ServerSnapshot.WorkspaceFolders;
        var removedUris = new List<Uri>(parameters.Event.Removed.Length);
        var removedProjectIds = new HashSet<AkburaProjectId>();
        foreach (var removed in parameters.Event.Removed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = AkburaProtocolMapper.ParseUri(removed.Uri);
            removedUris.Add(uri);
            if (folders.TryGetValue(uri, out var state))
            {
                foreach (var projectId in state.ProjectIds)
                {
                    removedProjectIds.Add(projectId);
                }
            }

            folders = folders.Remove(uri);
        }

        var addedStates = new List<AkburaWorkspaceFolderState>(
            parameters.Event.Added.Length);
        foreach (var added in parameters.Event.Added)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = AkburaProtocolMapper.ParseUri(added.Uri);
            var state = AkburaWorkspaceFolderState.Create(
                uri,
                string.IsNullOrWhiteSpace(added.Name)
                    ? GetFolderName(uri)
                    : added.Name);
            folders = folders.SetItem(uri, state);
            addedStates.Add(state);
        }

        var retainedProjectIds = folders.Values
            .SelectMany(static folder => folder.ProjectIds)
            .ToHashSet();
        foreach (var projectId in removedProjectIds)
        {
            if (!retainedProjectIds.Contains(projectId))
            {
                context.Services.Workspace.RemoveProject(projectId);
            }
        }

        var solution = context.Services.Workspace.CurrentSolution;
        var openDocuments = context.ServerSnapshot.OpenDocuments;
        foreach (var pair in context.ServerSnapshot.OpenDocuments)
        {
            if (pair.Value.ProjectId is { } projectId &&
                !solution.TryGetProject(projectId, out _))
            {
                openDocuments = openDocuments.SetItem(
                    pair.Key,
                    pair.Value with
                    {
                        ProjectId = null,
                        DocumentId = null,
                    });
            }
        }

        var addedArray = addedStates.ToImmutableArray();
        var removedArray = removedUris.ToImmutableArray();
        var next = context.ServerSnapshot.Next(solution) with
        {
            WorkspaceFolders = folders,
            OpenDocuments = openDocuments,
        };
        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next,
                afterCommit: token =>
                    context.Services.Projects.UpdateWorkspaceFoldersAsync(
                        addedArray,
                        removedArray,
                        token)));
    }

    private static string GetFolderName(Uri uri) =>
        uri.IsFile
            ? Path.GetFileName(uri.LocalPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar))
            : uri.Host;
}