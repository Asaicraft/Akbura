using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

internal sealed class AkburaCodeActionService : IAkburaCodeActionService
{
    public ImmutableArray<AkburaCodeAction> GetCodeActions(
        AkburaDocumentContext context,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var document = context.Document;
        var root = document.SyntaxTree.GetRootSyntax();
        if (root is not AkburaDocumentSyntax)
        {
            return ImmutableArray<AkburaCodeAction>.Empty;
        }

        var semanticModel = context.Project.Compilation
            .GetSemanticModel(document.SyntaxTree);
        using var actions =
            ImmutableArrayBuilder<AkburaCodeAction>.Rent();
        var equivalenceKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var diagnostic in
                 semanticModel.GetSemanticDiagnostics(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (diagnostic.Code !=
                    ErrorCodes.AKBURA_SEMANTIC_MarkupComponentNotFound ||
                !IntersectsOrTouches(
                    diagnostic.Span,
                    requestedSpan) ||
                diagnostic.Syntax is not
                    MarkupSimpleComponentNameSyntax componentSyntax)
            {
                continue;
            }

            var componentName = componentSyntax.Name.ToString().Trim();
            if (!IsSimpleName(componentName) ||
                diagnostic.Span.End > document.Text.Length)
            {
                continue;
            }

            var subjectText = document.Text.ToString(diagnostic.Span);
            foreach (var candidate in
                     semanticModel.LookupMarkupComponentImports(
                         componentName,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!AkburaUsingEditService.TryCreateNamespaceImportChange(
                        document.Text,
                        document.SyntaxTree,
                        candidate.NamespaceName,
                        diagnostic.Span.Start,
                        out var change))
                {
                    continue;
                }

                var equivalenceKey =
                    "AddNamespaceImport:" + candidate.NamespaceName;
                if (!equivalenceKeys.Add(equivalenceKey))
                {
                    continue;
                }

                actions.Add(new AkburaCodeAction(
                    AkburaCodeActionKind.AddNamespaceImport,
                    "Добавить using " + candidate.NamespaceName,
                    equivalenceKey,
                    subjectText,
                    candidate.NamespaceName,
                    diagnostic.Span,
                    ImmutableArray.Create(change)));
            }
        }

        return actions.ToImmutable();
    }

    private static bool IntersectsOrTouches(
        TextSpan diagnostic,
        TextSpan requested)
    {
        if (diagnostic.Length != 0 && requested.Length != 0)
        {
            return diagnostic.OverlapsWith(requested);
        }

        return requested.Start >= diagnostic.Start &&
               requested.Start <= diagnostic.End ||
               diagnostic.Start >= requested.Start &&
               diagnostic.Start <= requested.End;
    }

    private static bool IsSimpleName(string value)
    {
        if (value.Length == 0 ||
            !(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!char.IsLetterOrDigit(value[index]) &&
                value[index] != '_')
            {
                return false;
            }
        }

        return true;
    }
}
