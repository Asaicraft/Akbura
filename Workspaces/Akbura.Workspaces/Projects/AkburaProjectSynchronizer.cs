using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Applies Roslyn project contexts and Akbura source documents to one
/// <see cref="AkburaWorkspace"/> using immutable workspace transitions.
/// </summary>
public sealed class AkburaProjectSynchronizer
{
    private readonly AkburaWorkspace _workspace;
    private readonly RoslynProjectContextFactory _contextFactory;
    private readonly RoslynProjectDocumentLoader _documentLoader;

    public AkburaProjectSynchronizer(
        AkburaWorkspace workspace,
        RoslynProjectContextFactory? contextFactory = null,
        RoslynProjectDocumentLoader? documentLoader = null)
    {
        _workspace = workspace ??
            throw new ArgumentNullException(nameof(workspace));
        _contextFactory = contextFactory ??
            new RoslynProjectContextFactory();
        _documentLoader = documentLoader ??
            new RoslynProjectDocumentLoader();
    }

    public Task<ImmutableArray<AkburaLoadedProject>>
        SynchronizeSolutionAsync(
            Solution solution,
            Func<Uri, SourceText?>? openTextProvider,
            CancellationToken cancellationToken)
    {
        if (solution == null)
        {
            throw new ArgumentNullException(nameof(solution));
        }

        return SynchronizeProjectsAsync(
            solution.Projects.Where(static project =>
                project.Language == LanguageNames.CSharp),
            openTextProvider,
            cancellationToken);
    }

    public async Task<AkburaLoadedProject> SynchronizeProjectAsync(
        Project project,
        Func<Uri, SourceText?>? openTextProvider,
        Uri? excludedDocument,
        bool includeProjectReferences,
        CancellationToken cancellationToken)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (includeProjectReferences)
        {
            var references = project.ProjectReferences
                .Select(reference =>
                    project.Solution.GetProject(reference.ProjectId))
                .Where(static referenced =>
                    referenced != null &&
                    referenced.Language == LanguageNames.CSharp)
                .Select(static referenced => referenced!);

            await SynchronizeProjectsAsync(
                    references,
                    openTextProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var compilation = await project
            .GetCompilationAsync(cancellationToken)
            .ConfigureAwait(false);
        if (compilation is not CSharpCompilation csharpCompilation)
        {
            throw new InvalidOperationException(
                $"Project '{project.FilePath ?? project.Name}' " +
                "is not a C# project.");
        }

        return await SynchronizeProjectAsync(
                project,
                csharpCompilation,
                openTextProvider,
                excludedDocument,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AkburaLoadedProject> SynchronizeProjectAsync(
        Project project,
        CSharpCompilation compilation,
        Func<Uri, SourceText?>? openTextProvider,
        Uri? excludedDocument,
        CancellationToken cancellationToken)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        var context = _contextFactory.Create(
            project,
            compilation,
            excludedDocument?.LocalPath);
        var documents = await _documentLoader
            .LoadAsync(
                project,
                openTextProvider,
                excludedDocument,
                cancellationToken)
            .ConfigureAwait(false);

        var projectSnapshot = _workspace.AddOrUpdateProject(context);
        _workspace.SynchronizeProjectDocuments(
            projectSnapshot.Id,
            documents,
            cancellationToken);

        return new AkburaLoadedProject(
            context,
            documents,
            ImmutableArray<AkburaProjectLoadDiagnostic>.Empty);
    }

    private async Task<ImmutableArray<AkburaLoadedProject>>
        SynchronizeProjectsAsync(
            IEnumerable<Project> projects,
            Func<Uri, SourceText?>? openTextProvider,
            CancellationToken cancellationToken)
    {
        var ordered = OrderProjects(projects);
        var loaded = new AkburaLoadedProject[ordered.Length];

        for (var index = 0; index < ordered.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            loaded[index] = await SynchronizeProjectAsync(
                    ordered[index],
                    openTextProvider,
                    excludedDocument: null,
                    includeProjectReferences: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        using var results =
            ImmutableArrayBuilder<AkburaLoadedProject>.Rent(
                loaded.Length);
        results.AddRange(loaded);
        return results.ToImmutable();
    }

    private static ImmutableArray<Project> OrderProjects(
        IEnumerable<Project> projects)
    {
        var source = projects
            .Where(static project =>
                project.Language == LanguageNames.CSharp)
            .GroupBy(static project => project.Id)
            .Select(static group => group.First())
            .ToImmutableArray();
        var byId = source.ToDictionary(
            static project => project.Id);
        var visited = new HashSet<ProjectId>();
        var visiting = new HashSet<ProjectId>();
        var ordered = new List<Project>(source.Length);

        void Visit(Project project)
        {
            if (visited.Contains(project.Id) ||
                !visiting.Add(project.Id))
            {
                return;
            }

            foreach (var reference in project.ProjectReferences)
            {
                if (byId.TryGetValue(reference.ProjectId, out var referenced))
                {
                    Visit(referenced);
                }
            }

            visiting.Remove(project.Id);
            visited.Add(project.Id);
            ordered.Add(project);
        }

        foreach (var project in source)
        {
            Visit(project);
        }

        using var result =
            ImmutableArrayBuilder<Project>.Rent(ordered.Count);
        result.AddRange(ordered);
        return result.ToImmutable();
    }
}