using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

internal sealed class AkburaCSharpProjection
{
    public AkburaCSharpProjection(
        CompilationUnitSyntax root,
        TextSpan hostSpan,
        TextSpan projectedSpan,
        int projectedPosition,
        ImmutableArray<string> stateNames)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        HostSpan = hostSpan;
        ProjectedSpan = projectedSpan;
        ProjectedPosition = projectedPosition;
        StateNames = stateNames.IsDefault
            ? ImmutableArray<string>.Empty
            : stateNames;

        if (hostSpan.Length != projectedSpan.Length)
        {
            throw new ArgumentException(
                "The host and projected C# fragments must have equal lengths.",
                nameof(projectedSpan));
        }
    }

    public CompilationUnitSyntax Root { get; }

    public TextSpan HostSpan { get; }

    public TextSpan ProjectedSpan { get; }

    public int ProjectedPosition { get; }

    public ImmutableArray<string> StateNames { get; }

    public bool IsStateName(string name)
    {
        return !string.IsNullOrEmpty(name) &&
            StateNames.Contains(name, StringComparer.Ordinal);
    }

    public bool TryMapToHost(
        TextSpan projectedSpan,
        out TextSpan hostSpan)
    {
        if (projectedSpan.Start < ProjectedSpan.Start ||
            projectedSpan.End > ProjectedSpan.End)
        {
            hostSpan = default;
            return false;
        }

        hostSpan = new TextSpan(
            HostSpan.Start +
                projectedSpan.Start -
                ProjectedSpan.Start,
            projectedSpan.Length);
        return true;
    }

    public bool TryMapPositionToHost(
        int projectedPosition,
        out int hostPosition)
    {
        if (projectedPosition < ProjectedSpan.Start ||
            projectedPosition > ProjectedSpan.End)
        {
            hostPosition = default;
            return false;
        }

        hostPosition = HostSpan.Start +
            projectedPosition -
            ProjectedSpan.Start;
        return true;
    }
}

internal static class AkburaCSharpProjectionFactory
{
    public static bool TryCreate(
        AkburaSyntacticDocument syntacticDocument,
        AkburaDocumentContext semanticContext,
        AkburaCSharpCompletionContext completionContext,
        out AkburaCSharpProjection projection,
        CancellationToken cancellationToken = default)
    {
        if (syntacticDocument == null)
        {
            throw new ArgumentNullException(nameof(syntacticDocument));
        }

        if (semanticContext == null)
        {
            throw new ArgumentNullException(nameof(semanticContext));
        }

        if (completionContext.Kind !=
                AkburaCSharpCompletionContextKind.Expression ||
            !TryGetCurrentContext(
                syntacticDocument,
                semanticContext,
                out semanticContext))
        {
            projection = null!;
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var inlineExpression = semanticContext.Document.SyntaxTree
            .GetRootSyntax()
            .DescendantNodes()
            .OfType<InlineExpressionSyntax>()
            .FirstOrDefault(candidate =>
                candidate.FullSpan == completionContext.OwnerSpan &&
                candidate.Expression.Tokens.FullSpan ==
                    completionContext.HostSpan);
        if (inlineExpression == null)
        {
            projection = null!;
            return false;
        }

        var semanticModel = semanticContext.Project.Compilation
            .GetSemanticModel(
                semanticContext.Document.SyntaxTree);

        CSharpProbeProjection probe;
        try
        {
            probe = semanticModel.CreateCSharpCompletionProjection(
                inlineExpression,
                completionContext.RelativePosition);
        }
        catch (InvalidOperationException)
        {
            projection = null!;
            return false;
        }
        catch (ArgumentException)
        {
            projection = null!;
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (probe.ProjectedSpan.Length !=
            completionContext.HostSpan.Length)
        {
            projection = null!;
            return false;
        }

        projection = new AkburaCSharpProjection(
            probe.Root,
            completionContext.HostSpan,
            probe.ProjectedSpan,
            probe.ProjectedPosition,
            probe.StateNames);
        return true;
    }

    private static bool TryGetCurrentContext(
        AkburaSyntacticDocument syntacticDocument,
        AkburaDocumentContext semanticContext,
        out AkburaDocumentContext currentContext)
    {
        var semanticDocument = semanticContext.Document;
        if (semanticDocument.Text.ContentEquals(
                syntacticDocument.Text))
        {
            currentContext = semanticContext;
            return true;
        }

        if (!string.Equals(
                semanticDocument.FilePath,
                syntacticDocument.FilePath,
                StringComparison.OrdinalIgnoreCase) ||
            semanticDocument.SyntaxTree.Kind !=
                syntacticDocument.SyntaxTree.Kind)
        {
            currentContext = null!;
            return false;
        }

        var currentDocument = new AkburaDocumentSnapshot(
            semanticDocument.Id,
            semanticDocument.ProjectId,
            semanticDocument.Uri,
            semanticDocument.FilePath,
            VersionStamp.Create(),
            syntacticDocument.Text,
            syntacticDocument.SyntaxTree,
            semanticDocument.IsOpen);

        try
        {
            var currentProject = semanticContext.Project
                .ReplaceDocument(currentDocument);
            currentContext = new AkburaDocumentContext(
                currentProject,
                currentDocument);
            return true;
        }
        catch (ArgumentException)
        {
            currentContext = null!;
            return false;
        }
        catch (InvalidOperationException)
        {
            currentContext = null!;
            return false;
        }
    }
}
