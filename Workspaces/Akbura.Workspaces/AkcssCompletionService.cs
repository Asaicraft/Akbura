using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

internal sealed class AkcssCompletionService
{
    private const int MaximumCompletionItems = 50;

    public AkburaCompletionResult GetCompletions(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        CancellationToken cancellationToken)
    {
        var context = document.GetAkcssCompletionContext(
            position,
            cancellationToken);
        if (context.IsDefault)
        {
            return new AkburaCompletionResult(
                context.ApplicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty);
        }

        var semanticModel = semanticContext?.Document.SyntaxTree.Kind ==
                SyntaxTreeKind.Akcss
            ? semanticContext.Project.Compilation.GetSemanticModel(
                semanticContext.Document.SyntaxTree)
            : null;

        var items = context.Kind switch
        {
            AkcssCompletionContextKind.TopLevel =>
                GetTopLevelItems(context),

            AkcssCompletionContextKind.SelectorSnippet =>
                GetSelectorItems(context),

            AkcssCompletionContextKind.BodyMember =>
                GetBodyMemberItems(
                    document,
                    semanticModel,
                    context,
                    cancellationToken),

            AkcssCompletionContextKind.PropertyName =>
                GetPropertyItems(
                    semanticModel,
                    context,
                    cancellationToken),

            AkcssCompletionContextKind.PropertyValue =>
                GetValueItems(context),

            AkcssCompletionContextKind.ApplyItem =>
                GetApplyItems(
                    semanticModel,
                    context,
                    cancellationToken),

            AkcssCompletionContextKind.AkcssModuleName =>
                GetModuleItems(
                    semanticContext,
                    context,
                    cancellationToken),

            _ => ImmutableArray<AkburaCompletionItem>.Empty,
        };

        var hasSemanticCatalog = context.Kind ==
                AkcssCompletionContextKind.AkcssModuleName
            ? semanticContext != null
            : semanticModel != null;
        return new AkburaCompletionResult(
            context.ApplicableSpan,
            items,
            isIncomplete: IsSemanticContext(context.Kind) &&
                !hasSemanticCatalog);
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetTopLevelItems(AkcssSyntacticCompletionContext context)
    {
        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        AddKeyword(
            items,
            context.Prefix,
            "@using",
            "@using ;",
            "Imports a C# namespace or AKCSS module.",
            caretOffsetFromEnd: 1,
            triggerCompletionAfterInsert: true);
        AddKeyword(
            items,
            context.Prefix,
            "@utilities",
            "@utilities {\n\n}",
            "Declares AKCSS utilities.",
            caretOffsetFromEnd: 2);
        return items.ToImmutable();
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetSelectorItems(AkcssSyntacticCompletionContext context)
    {
        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        if (MatchesPrefix(".class", context.Prefix))
        {
            items.Add(new AkburaCompletionItem(
                ".class",
                ".class {\n\n}",
                AkburaCompletionKind.AkcssStyle,
                "Declares an untyped AKCSS style.",
                descriptionFactory: null,
                priority: 5,
                caretOffsetFromEnd: 2));
        }

        return items.ToImmutable();
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetBodyMemberItems(
            AkburaSyntacticDocument document,
            AkburaSemanticModel? semanticModel,
            AkcssSyntacticCompletionContext context,
            CancellationToken cancellationToken)
    {
        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        items.AddRange(GetPropertyItems(
            semanticModel,
            context,
            cancellationToken));

        AddKeyword(
            items,
            context.Prefix,
            "@apply",
            "@apply ;",
            "Applies another compatible AKCSS style or utility.",
            caretOffsetFromEnd: 1,
            triggerCompletionAfterInsert: true);
        AddKeyword(
            items,
            context.Prefix,
            "@if",
            "@if()",
            "Adds conditional AKCSS operations.",
            caretOffsetFromEnd: 1,
            triggerCompletionAfterInsert: true);

        if (!HasInterceptDirective(
                document,
                context.ContainingDeclarationSpan))
        {
            AddKeyword(
                items,
                context.Prefix,
                "@intercept",
                "@intercept ;",
                "Changes the target type used by this AKCSS declaration.",
                caretOffsetFromEnd: 1,
                triggerCompletionAfterInsert: true);
        }

        return items.ToImmutable();
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetPropertyItems(
            AkburaSemanticModel? semanticModel,
            AkcssSyntacticCompletionContext context,
            CancellationToken cancellationToken)
    {
        if (semanticModel == null)
        {
            return ImmutableArray<AkburaCompletionItem>.Empty;
        }

        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        foreach (var candidate in semanticModel
                     .LookupAkcssPropertiesForCompletion(
                         context.ContainingDeclarationSpan,
                         context.Qualifier,
                         cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesPrefix(
                    candidate.InsertName,
                    context.Prefix))
            {
                continue;
            }

            var description = candidate.IsAttached
                ? $"Attached property {candidate.DisplayName}: " +
                  candidate.TypeDisplay
                : $"Property {candidate.DisplayName}: " +
                  candidate.TypeDisplay;
            items.Add(new AkburaCompletionItem(
                candidate.DisplayName,
                candidate.InsertName + ": ",
                AkburaCompletionKind.Property,
                description,
                descriptionFactory: null,
                filterText: candidate.InsertName,
                suffix: candidate.TypeDisplay + " \u00B7 " +
                    candidate.OwnerTypeDisplay,
                priority: candidate.IsAttached ? 1 : 0,
                triggerCompletionAfterInsert: true));
            if (items.Count == MaximumCompletionItems)
            {
                break;
            }
        }

        return items.ToImmutable();
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetApplyItems(
            AkburaSemanticModel? semanticModel,
            AkcssSyntacticCompletionContext context,
            CancellationToken cancellationToken)
    {
        if (semanticModel == null)
        {
            return ImmutableArray<AkburaCompletionItem>.Empty;
        }

        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in semanticModel
                     .LookupAkcssApplyItemsForCompletion(
                         context.ContainingDeclarationSpan,
                         cancellationToken)
                     .OrderBy(static candidate => candidate.Priority)
                     .ThenBy(
                         static candidate => candidate.DisplayText,
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesApplyPrefix(
                    candidate.InsertText,
                    context.Prefix) ||
                !seen.Add(candidate.DisplayText + "\0" +
                    candidate.SourceModule))
            {
                continue;
            }

            var insertText = context.Prefix.StartsWith(
                    candidate.InsertText,
                    StringComparison.OrdinalIgnoreCase)
                ? context.Prefix
                : candidate.InsertText;
            items.Add(new AkburaCompletionItem(
                candidate.DisplayText,
                insertText,
                candidate.IsUtility
                    ? AkburaCompletionKind.TailwindUtility
                    : AkburaCompletionKind.AkcssStyle,
                candidate.Symbol.ToDisplayString(),
                descriptionFactory: null,
                filterText: candidate.InsertText,
                suffix: (candidate.IsUtility
                    ? "utility"
                    : "style") + " \u00B7 " +
                    candidate.SourceModule,
                priority: candidate.Priority));
            if (items.Count == MaximumCompletionItems)
            {
                break;
            }
        }

        return items.ToImmutable();
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetModuleItems(
            AkburaDocumentContext? semanticContext,
            AkcssSyntacticCompletionContext context,
            CancellationToken cancellationToken)
    {
        if (semanticContext == null)
        {
            return ImmutableArray<AkburaCompletionItem>.Empty;
        }

        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        foreach (var name in semanticContext.Project.Compilation
                     .GetAvailableAkcssModuleNames(cancellationToken)
                     .OrderBy(static name => name, StringComparer.Ordinal))
        {
            if (!MatchesPrefix(name, context.Prefix))
            {
                continue;
            }

            items.Add(new AkburaCompletionItem(
                name,
                name,
                AkburaCompletionKind.AkcssModule,
                $"Imports AKCSS module '{name}'.",
                descriptionFactory: null,
                suffix: "AKCSS module",
                priority: 0));
            if (items.Count == MaximumCompletionItems)
            {
                break;
            }
        }

        return items.ToImmutable();
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetValueItems(AkcssSyntacticCompletionContext context)
    {
        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        foreach (var value in new[] { "true", "false", "null" })
        {
            if (MatchesPrefix(value, context.Prefix))
            {
                items.Add(new AkburaCompletionItem(
                    value,
                    value,
                    AkburaCompletionKind.AkcssValue,
                    $"C# value '{value}'.",
                    descriptionFactory: null,
                    priority: 20));
            }
        }

        return items.ToImmutable();
    }

    private static void AddKeyword(
        ImmutableArrayBuilder<AkburaCompletionItem> items,
        string prefix,
        string displayText,
        string insertText,
        string description,
        int caretOffsetFromEnd = 0,
        bool triggerCompletionAfterInsert = false)
    {
        if (!MatchesPrefix(displayText, prefix))
        {
            return;
        }

        items.Add(new AkburaCompletionItem(
            displayText,
            insertText,
            AkburaCompletionKind.Keyword,
            description,
            descriptionFactory: null,
            filterText: displayText,
            priority: 0,
            caretOffsetFromEnd: caretOffsetFromEnd,
            triggerCompletionAfterInsert:
                triggerCompletionAfterInsert));
    }

    private static bool HasInterceptDirective(
        AkburaSyntacticDocument document,
        TextSpan declarationSpan)
    {
        return document.SyntaxTree.GetRootSyntax()
            .DescendantNodes()
            .Where(node => node.FullSpan == declarationSpan)
            .SelectMany(static node => node.DescendantNodes())
            .Any(static node => node is AkcssInterceptDirectiveSyntax);
    }

    private static bool MatchesPrefix(
        string candidate,
        string prefix)
    {
        var normalizedPrefix = prefix.TrimStart('@');
        var normalizedCandidate = candidate.TrimStart('@');
        return normalizedPrefix.Length == 0 ||
            normalizedCandidate.StartsWith(
                normalizedPrefix,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesApplyPrefix(
        string candidate,
        string prefix)
    {
        return MatchesPrefix(candidate, prefix) ||
            prefix.StartsWith(
                candidate,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSemanticContext(
        AkcssCompletionContextKind kind)
    {
        return kind is
            AkcssCompletionContextKind.BodyMember or
            AkcssCompletionContextKind.PropertyName or
            AkcssCompletionContextKind.ApplyItem or
            AkcssCompletionContextKind.AkcssModuleName;
    }
}
