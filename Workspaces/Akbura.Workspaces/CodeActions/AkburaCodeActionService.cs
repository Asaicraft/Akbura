using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.CodeActions;

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
        if (root is not (
                AkburaDocumentSyntax or
                AkcssDocumentSyntax))
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
            if (!IntersectsOrTouches(
                    diagnostic.Span,
                    requestedSpan))
            {
                continue;
            }

            if (diagnostic.Code ==
                    ErrorCodes.AKBURA_SEMANTIC_MarkupComponentNotFound &&
                diagnostic.Syntax is
                    MarkupSimpleComponentNameSyntax componentSyntax)
            {
                AddMarkupComponentImports(
                    semanticModel,
                    document,
                    diagnostic,
                    componentSyntax,
                    equivalenceKeys,
                    actions,
                    cancellationToken);
                continue;
            }

            if (diagnostic.Code ==
                    ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityNotFound &&
                diagnostic.Syntax is TailwindAttributeSyntax attribute)
            {
                AddAkcssImports(
                    semanticModel,
                    document,
                    diagnostic,
                    attribute,
                    equivalenceKeys,
                    actions,
                    cancellationToken);
                continue;
            }

            if (diagnostic.Code ==
                    ErrorCodes.AKBURA_SEMANTIC_AkcssApplyItemNotFound &&
                diagnostic.Syntax is AkcssApplyDirectiveSyntax apply)
            {
                AddAkcssApplyImports(
                    semanticModel,
                    document,
                    diagnostic,
                    apply,
                    equivalenceKeys,
                    actions,
                    cancellationToken);
            }
        }

        return actions.ToImmutable();
    }

    private static void AddMarkupComponentImports(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSnapshot document,
        AkburaSemanticDiagnostic diagnostic,
        MarkupSimpleComponentNameSyntax componentSyntax,
        HashSet<string> equivalenceKeys,
        ImmutableArrayBuilder<AkburaCodeAction> actions,
        CancellationToken cancellationToken)
    {
        var componentName = componentSyntax.Name.ToString().Trim();
        if (!IsSimpleName(componentName) ||
            diagnostic.Span.End > document.Text.Length)
        {
            return;
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

    private static void AddAkcssImports(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSnapshot document,
        AkburaSemanticDiagnostic diagnostic,
        TailwindAttributeSyntax attribute,
        HashSet<string> equivalenceKeys,
        ImmutableArrayBuilder<AkburaCodeAction> actions,
        CancellationToken cancellationToken)
    {
        if (diagnostic.Span.End > document.Text.Length)
        {
            return;
        }

        var containingComponent =
            semanticModel.GetContainingMarkupComponentSymbol(attribute);
        var compilation = semanticModel.Compilation;
        var subjectText = document.Text.ToString(diagnostic.Span);

        foreach (var moduleName in
                 compilation.GetAvailableAkcssModuleNames(
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var modules = compilation.LookupAkcssModulesByLogicalName(
                moduleName,
                cancellationToken);
            if (modules.Length != 1 ||
                !ContainsCompatibleUtility(
                    modules[0],
                    attribute,
                    containingComponent))
            {
                continue;
            }

            if (!AkburaUsingEditService.TryCreateNamespaceImportChange(
                    document.Text,
                    document.SyntaxTree,
                    moduleName,
                    diagnostic.Span.Start,
                    out var change))
            {
                continue;
            }

            var equivalenceKey = "AddAkcssImport:" + moduleName;
            if (!equivalenceKeys.Add(equivalenceKey))
            {
                continue;
            }

            actions.Add(new AkburaCodeAction(
                AkburaCodeActionKind.AddAkcssImport,
                "Добавить AKCSS import " + moduleName,
                equivalenceKey,
                subjectText,
                moduleName,
                diagnostic.Span,
                ImmutableArray.Create(change)));
        }
    }

    private static void AddAkcssApplyImports(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSnapshot document,
        AkburaSemanticDiagnostic diagnostic,
        AkcssApplyDirectiveSyntax apply,
        HashSet<string> equivalenceKeys,
        ImmutableArrayBuilder<AkburaCodeAction> actions,
        CancellationToken cancellationToken)
    {
        if (diagnostic.Span.End > document.Text.Length ||
            diagnostic.Parameters.Length == 0 ||
            diagnostic.Parameters[0] is not string item ||
            string.IsNullOrWhiteSpace(item))
        {
            return;
        }

        var containingSymbol =
            semanticModel.GetContainingAkcssSymbol(apply);
        if (containingSymbol == null)
        {
            return;
        }

        var compilation = semanticModel.Compilation;
        var subjectText = item.Trim();
        foreach (var moduleName in
                 compilation.GetAvailableAkcssModuleNames(
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var modules = compilation.LookupAkcssModulesByLogicalName(
                moduleName,
                cancellationToken);
            if (modules.Length != 1 ||
                !ContainsCompatibleApplyItem(
                    modules[0],
                    subjectText,
                    containingSymbol))
            {
                continue;
            }

            if (!AkburaUsingEditService.TryCreateNamespaceImportChange(
                    document.Text,
                    document.SyntaxTree,
                    moduleName,
                    diagnostic.Span.Start,
                    out var change))
            {
                continue;
            }

            var equivalenceKey = "AddAkcssImport:" + moduleName;
            if (!equivalenceKeys.Add(equivalenceKey))
            {
                continue;
            }

            actions.Add(new AkburaCodeAction(
                AkburaCodeActionKind.AddAkcssImport,
                "Добавить AKCSS import " + moduleName,
                equivalenceKey,
                subjectText,
                moduleName,
                diagnostic.Span,
                ImmutableArray.Create(change)));
        }
    }

    private static bool ContainsCompatibleApplyItem(
        IAkcssModuleSymbol module,
        string item,
        IAkcssSymbol containingSymbol)
    {
        foreach (var candidate in module.AkcssSymbols)
        {
            if (!IsApplyTargetCompatible(
                    candidate,
                    containingSymbol))
            {
                continue;
            }

            if (candidate is not ITailwindUtilitySymbol utility)
            {
                if (string.Equals(
                        candidate.ClassName,
                        item,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                continue;
            }

            if (utility.Parameters.Length == 0 &&
                string.Equals(
                    utility.Name,
                    item,
                    StringComparison.Ordinal))
            {
                return true;
            }

            var argumentCount = 1;
            for (var dashIndex = item.LastIndexOf('-');
                 dashIndex > 0;
                 dashIndex = item.LastIndexOf(
                     '-',
                     dashIndex - 1),
                 argumentCount++)
            {
                if (utility.Parameters.Length == argumentCount &&
                    string.Equals(
                        utility.Name,
                        item[..dashIndex],
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsApplyTargetCompatible(
        IAkcssSymbol candidate,
        IAkcssSymbol containingSymbol)
    {
        if (!candidate.HasTargetType ||
            !containingSymbol.HasTargetType)
        {
            return true;
        }

        return containingSymbol.TargetType.Symbol is
                Microsoft.CodeAnalysis.ITypeSymbol containingType &&
            candidate.TargetType.Symbol is
                Microsoft.CodeAnalysis.ITypeSymbol candidateType &&
            AkburaSemanticModel.IsAssignableTo(
                containingType,
                candidateType);
    }
    private static bool ContainsCompatibleUtility(
        IAkcssModuleSymbol module,
        TailwindAttributeSyntax attribute,
        IMarkupComponentSymbol? containingComponent)
    {
        foreach (var symbol in module.AkcssSymbols)
        {
            if (symbol is ITailwindUtilitySymbol utility &&
                MatchesUtilitySyntax(utility, attribute) &&
                IsTargetCompatible(utility, containingComponent))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesUtilitySyntax(
        ITailwindUtilitySymbol utility,
        TailwindAttributeSyntax attribute)
    {
        if (attribute is TailwindFlagAttributeSyntax flag)
        {
            return utility.Parameters.Length == 0 &&
                string.Equals(
                    utility.Name,
                    flag.Name.Identifier.ValueText,
                    StringComparison.Ordinal);
        }

        if (attribute is not TailwindFullAttributeSyntax full)
        {
            return false;
        }

        var staticSegmentCount = 0;
        foreach (var segment in full.Segments)
        {
            if (segment is not (
                    TailwindIdentifierSegmentSyntax or
                    TailwindNumericSegmentSyntax))
            {
                break;
            }

            staticSegmentCount++;
        }

        for (var consumed = staticSegmentCount;
             consumed >= 0;
             consumed--)
        {
            if (utility.Parameters.Length !=
                    full.Segments.Count - consumed)
            {
                continue;
            }

            var candidateName = full.Name.Identifier.ValueText;
            for (var index = 0; index < consumed; index++)
            {
                candidateName += "-" +
                    full.Segments[index].ToFullString().Trim();
            }

            if (string.Equals(
                    utility.Name,
                    candidateName,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTargetCompatible(
        IAkcssSymbol symbol,
        IMarkupComponentSymbol? containingComponent)
    {
        if (!symbol.HasTargetType)
        {
            return true;
        }

        return containingComponent?.ComponentType != null &&
            symbol.TargetType.Symbol is
                Microsoft.CodeAnalysis.ITypeSymbol targetType &&
            AkburaSemanticModel.IsAssignableTo(
                containingComponent.ComponentType,
                targetType);
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