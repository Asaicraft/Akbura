namespace Akbura.LanguageServer.Projects;

internal static class AkburaInternalMethods
{
    public const string ApplyLoadedProjects =
        "akbura/internal/applyLoadedProjects";
}

internal sealed class AkburaApplyLoadedProjectsHandler :
    AkburaLspHandler<AkburaProjectLoadResult, object?>
{
    public override string Method =>
        AkburaInternalMethods.ApplyLoadedProjects;

    public override bool MutatesServerState => true;

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(
        AkburaProjectLoadResult parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var workspace = context.Services.Workspace;
        var openDocuments = context.ServerSnapshot.OpenDocuments;
        var diskTexts = new Dictionary<Uri, SourceText>(
            AkburaUriComparer.Instance);
        var existingFolder = context.ServerSnapshot.WorkspaceFolders
            .TryGetValue(parameters.WorkspaceFolder, out var currentFolder)
                ? currentFolder
                : AkburaWorkspaceFolderState.Create(
                    parameters.WorkspaceFolder,
                    parameters.WorkspaceFolderName);
        var projectIds = parameters.Succeeded
            ? new List<AkburaProjectId>(parameters.Projects.Length)
            : existingFolder.ProjectIds.ToList();

        if (parameters.Succeeded)
        {
            foreach (var loadedProject in parameters.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var project = workspace.AddOrUpdateProject(
                    loadedProject.Context);
                projectIds.Add(project.Id);

                var inputs = new AkburaDocumentInput[
                    loadedProject.Documents.Length];
                for (var index = 0;
                     index < loadedProject.Documents.Length;
                     index++)
                {
                    var input = loadedProject.Documents[index];
                    diskTexts[input.Uri] = input.Text;
                    inputs[index] = openDocuments.TryGetValue(
                            input.Uri,
                            out var openDocument)
                        ? new AkburaDocumentInput(
                            input.Uri,
                            openDocument.Text)
                        : input;
                }

                workspace.SynchronizeProjectDocuments(
                    project.Id,
                    inputs.ToImmutableArray(),
                    cancellationToken);
            }
        }

        if (parameters.Succeeded)
        {
            var retainedByOtherFolders = context.ServerSnapshot
                .WorkspaceFolders
                .Where(pair => !AkburaUriComparer.Instance.Equals(
                    pair.Key,
                    parameters.WorkspaceFolder))
                .SelectMany(static pair => pair.Value.ProjectIds)
                .ToHashSet();
            foreach (var oldProjectId in existingFolder.ProjectIds)
            {
                if (!projectIds.Contains(oldProjectId) &&
                    !retainedByOtherFolders.Contains(oldProjectId))
                {
                    workspace.RemoveProject(oldProjectId);
                }
            }
        }

        var solution = workspace.CurrentSolution;
        foreach (var pair in openDocuments)
        {
            if (!solution.TryGetDocument(
                    pair.Key,
                    out var document))
            {
                continue;
            }

            diskTexts.TryGetValue(pair.Key, out var projectText);
            openDocuments = openDocuments.SetItem(
                pair.Key,
                pair.Value with
                {
                    ProjectId = document.ProjectId,
                    DocumentId = document.Id,
                    ProjectText = projectText ??
                        pair.Value.ProjectText,
                });
        }

        var folder = existingFolder with
        {
            SolutionOrProjectPath =
                parameters.SolutionOrProjectPath,
            ProjectIds = projectIds.ToImmutableArray(),
            LoadState = parameters.Succeeded
                ? AkburaWorkspaceFolderLoadState.Loaded
                : AkburaWorkspaceFolderLoadState.Failed,
            ErrorMessage = parameters.ErrorMessage,
        };
        var folders = context.ServerSnapshot.WorkspaceFolders.SetItem(
            parameters.WorkspaceFolder,
            folder);
        var next = context.ServerSnapshot
            .Next(solution) with
            {
                OpenDocuments = openDocuments,
                WorkspaceFolders = folders,
            };

        Func<CancellationToken, Task> afterCommit = async token =>
        {
            if (!parameters.Succeeded)
            {
                await context.Services.Client.NotifyAsync(
                        "window/showMessage",
                        new ShowMessageParams
                        {
                            Type = 1,
                            Message = parameters.ErrorMessage ??
                                "Akbura project loading failed.",
                        },
                        token)
                    .ConfigureAwait(false);
                return;
            }

            foreach (var diagnostic in parameters.Diagnostics)
            {
                await context.Services.Client.NotifyAsync(
                        "window/showMessage",
                        new ShowMessageParams
                        {
                            Type = diagnostic.Severity ==
                                AkburaProjectLoadDiagnosticSeverity.Error
                                    ? 1
                                    : 2,
                            Message = diagnostic.Message,
                        },
                        token)
                    .ConfigureAwait(false);
            }

            await context.Services.Diagnostics
                .PublishAllSemanticAsync(token)
                .ConfigureAwait(false);
        };

        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next,
                afterCommit));
    }
}
