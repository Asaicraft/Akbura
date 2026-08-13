using Akbura.Pools;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Immutable;

namespace Akbura.Workspaces.MSBuild;

public sealed class MsBuildProjectContextProvider :
    IProjectContextProvider
{
    private readonly MSBuildWorkspace _workspace;
    private bool _isDisposed;

    public MsBuildProjectContextProvider()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        _workspace = MSBuildWorkspace.Create();
    }

    public event EventHandler<ProjectContextChangedEventArgs>?
        Changed;

    public async Task<ProjectContext> OpenProjectAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var project = await _workspace
            .OpenProjectAsync(
                projectPath,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return await CreateContextAsync(
                project,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ImmutableArray<ProjectContext>>
        OpenSolutionAsync(
            string solutionPath,
            CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var solution = await _workspace
            .OpenSolutionAsync(
                solutionPath,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var projects = solution.Projects
            .Where(static project =>
                project.Language == LanguageNames.CSharp)
            .ToArray();
        var contexts = new ProjectContext[projects.Length];
        for (var index = 0; index < projects.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            contexts[index] = await CreateContextAsync(
                    projects[index],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        using var builder =
            ImmutableArrayBuilder<ProjectContext>.Rent(contexts.Length);
        builder.AddRange(contexts);
        return builder.ToImmutable();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _workspace.Dispose();
    }

    private static async Task<ProjectContext> CreateContextAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var compilation = await project
            .GetCompilationAsync(cancellationToken)
            .ConfigureAwait(false);

        if (compilation is not CSharpCompilation csharpCompilation)
        {
            throw new InvalidOperationException(
                $"Project '{project.FilePath ?? project.Name}' " +
                "is not a C# project.");
        }

        var projectFilePath =
            project.FilePath ?? string.Empty;

        var projectDirectory =
            Path.GetDirectoryName(projectFilePath) ??
            Environment.CurrentDirectory;

        // MSBuildWorkspace does not expose evaluated RootNamespace directly.
        // project.Name is only a fallback. A later provider may read the
        // evaluated MSBuild property explicitly.
        var rootNamespace = project.Name;

        return new ProjectContext(
            project.Id,
            projectFilePath,
            projectDirectory,
            rootNamespace,
            csharpCompilation,
            project.ProjectReferences.ToImmutableArray());
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(
                nameof(MsBuildProjectContextProvider));
        }
    }
}
