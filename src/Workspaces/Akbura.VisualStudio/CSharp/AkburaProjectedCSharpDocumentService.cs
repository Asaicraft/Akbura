using Akbura.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using System.ComponentModel.Composition;
using System.Text;

namespace Akbura.VisualStudio.CSharp;

[Export(typeof(AkburaProjectedCSharpDocumentService))]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class AkburaProjectedCSharpDocumentService : IDisposable
{
    private readonly AkburaVisualStudioWorkspace _workspaceHost;

    private readonly AkburaProjectedCSharpDocumentCache _cache;

    [ImportingConstructor]
    public AkburaProjectedCSharpDocumentService(
        AkburaVisualStudioWorkspace workspaceHost)
    {
        _workspaceHost = workspaceHost ??
            throw new ArgumentNullException(nameof(workspaceHost));
        _cache = new AkburaProjectedCSharpDocumentCache(workspaceHost);
    }

    public async Task<AkburaProjectedCSharpDocument?> GetProjectedDocumentAsync(
        ITextSnapshot snapshot,
        AkburaSyntacticDocument syntacticDocument,
        AkburaDocumentContext? semanticContext,
        AkburaEmbeddedCSharpContext context,
        CancellationToken cancellationToken)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (syntacticDocument == null)
        {
            throw new ArgumentNullException(nameof(syntacticDocument));
        }

        if (semanticContext == null)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.CSharp,
                "Projection unavailable: no semantic " +
                "project snapshot has been published yet.");
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var projected = await _cache.GetOrCreateAsync(
                snapshot,
                context,
                () => Task.FromResult(CreateProjectedDocument(
                    snapshot,
                    syntacticDocument,
                    semanticContext,
                    context,
                    cancellationToken)))
            .ConfigureAwait(false);
        if (projected == null ||
            !projected.Projection.TryMapPositionToProjected(
                context.HostPosition,
                out var projectedPosition))
        {
            return null;
        }

        var projection = projected.Projection.WithProjectedPosition(
            projectedPosition);
        return ReferenceEquals(projection, projected.Projection)
            ? projected
            : new AkburaProjectedCSharpDocument(
                projected.HostSnapshot,
                projected.HostDocument,
                projected.SemanticContext,
                projected.RoslynDocument,
                projection);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private AkburaProjectedCSharpDocument? CreateProjectedDocument(
        ITextSnapshot snapshot,
        AkburaSyntacticDocument syntacticDocument,
        AkburaDocumentContext semanticContext,
        AkburaEmbeddedCSharpContext context,
        CancellationToken cancellationToken)
    {
        if (!AkburaCSharpProjectionFactory.TryCreate(
                syntacticDocument,
                semanticContext,
                context,
                out var projection,
                out var failureReason,
                cancellationToken))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.CSharp,
                "Projection could not be created: " +
                failureReason);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var project = _workspaceHost.FindRoslynProjectForDocument(
            syntacticDocument.FilePath);
        if (project == null)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.CSharp,
                "Roslyn project was not found for the " +
                "current Akbura document.");
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(
                syntacticDocument.FilePath) +
            ".AkburaProjection.cs";
        var filePath = syntacticDocument.FilePath + ".projection.cs";
        var text = SourceText.From(
            projection.Root.ToFullString(),
            Encoding.UTF8);
        var roslynDocument = project
            .AddDocument(name, text, filePath: filePath)
            .WithSyntaxRoot(projection.Root);

        return new AkburaProjectedCSharpDocument(
            snapshot,
            syntacticDocument,
            semanticContext,
            roslynDocument,
            projection);
    }
}
