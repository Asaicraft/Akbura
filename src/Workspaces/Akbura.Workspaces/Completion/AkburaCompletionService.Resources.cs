using Akbura.Pools;
using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Workspaces.Documents;
using Akbura.Workspaces.Resources;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Workspaces.Completion;

internal sealed partial class AkburaCompletionService
{
    private readonly ResourceCompletionIndex _resourceCompletionIndex =
        new();

    private static bool IsAvaloniaResourceKeyContext(AkburaDocumentContext? semanticContext, AkburaResourceKeyCompletionContext context)
    {
        if (semanticContext == null)
        {
            return false;
        }

        var semanticModel = semanticContext.Project.Compilation
            .GetSemanticModel(
                semanticContext.Document.SyntaxTree);
        var semanticExtension = FindMarkupExtensionSyntax(
            semanticModel,
            context.Extension.Span);
        return semanticExtension != null &&
            semanticModel.IsAvaloniaResourceMarkupExtensionForCompletion(
                semanticExtension);
    }

    private AkburaCompletionResult CreateResourceKeyResult(AkburaDocumentContext? semanticContext, AkburaResourceKeyCompletionContext context, CancellationToken cancellationToken)
    {
        if (semanticContext == null)
        {
            return new AkburaCompletionResult(
                context.ApplicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty,
                isIncomplete: true);
        }

        var candidates = _resourceCompletionIndex.GetCandidates(
            semanticContext,
            context.ApplicableSpan.Start,
            cancellationToken);
        var semanticModel = semanticContext.Project.Compilation
            .GetSemanticModel(
                semanticContext.Document.SyntaxTree);
        var expectedType = GetExpectedResourceType(
            semanticModel,
            context,
            cancellationToken);
        expectedType = MapTypeToCompilation(
            expectedType,
            semanticContext.Project.CSharpCompilation);
        using var matches =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        var ranked =
            ArrayBuilder<RankedResourceCandidate>.GetInstance(
                candidates.Length);
        try
        {
            var matchCount = 0;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MatchesPrefix(
                        candidate.Key,
                        context.Prefix))
                {
                    continue;
                }

                matchCount++;
                ranked.Add(new RankedResourceCandidate(
                    candidate,
                    GetResourceCandidatePriority(
                        candidate,
                        expectedType,
                        semanticContext.Project.CSharpCompilation)));
            }

            ranked.Sort(RankedResourceCandidateComparer.Instance);
            for (var index = 0; index < ranked.Count && matches.Count < MaximumCompletionItems; index++)
            {
                var candidate = ranked[index];

                matches.Add(CreateResourceCompletionItem(
                    candidate.Candidate,
                    context,
                    candidate.Priority));
            }

            return new AkburaCompletionResult(
                context.ApplicableSpan,
                matches.ToImmutable(),
                isIncomplete: matchCount > matches.Count);
        }
        finally
        {
            ranked.Free();
        }
    }

    private static int GetResourceCandidatePriority(ResourceCompletionCandidate candidate, ITypeSymbol? expectedType, Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation)
    {
        if (expectedType == null)
        {
            return candidate.Priority;
        }

        foreach (var origin in candidate.Origins)
        {
            if (origin.ResourceType is not { } resourceType)
            {
                continue;
            }

            var conversion = compilation.ClassifyConversion(
                resourceType,
                expectedType);
            if (conversion.IsImplicit)
            {
                return candidate.Priority - 5;
            }
        }

        return candidate.Priority;
    }

    private static ITypeSymbol? MapTypeToCompilation(ITypeSymbol? type, Compilation compilation)
    {
        if (type == null)
        {
            return null;
        }

        if (type is IDynamicTypeSymbol)
        {
            return compilation.DynamicType;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            var elementType = MapTypeToCompilation(
                arrayType.ElementType,
                compilation);
            return elementType == null
                ? null
                : compilation.CreateArrayTypeSymbol(
                    elementType,
                    arrayType.Rank);
        }

        if (type is IPointerTypeSymbol pointerType)
        {
            var pointedAtType = MapTypeToCompilation(
                pointerType.PointedAtType,
                compilation);
            return pointedAtType == null
                ? null
                : compilation.CreatePointerTypeSymbol(pointedAtType);
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return null;
        }

        var definition = compilation.GetTypeByMetadataName(
            GetMetadataName(namedType.OriginalDefinition));
        if (definition == null ||
            namedType.TypeArguments.Length == 0)
        {
            return definition;
        }

        if (definition.TypeParameters.Length !=
            namedType.TypeArguments.Length)
        {
            return null;
        }

        using var arguments =
            ImmutableArrayBuilder<ITypeSymbol>.Rent();
        foreach (var argument in namedType.TypeArguments)
        {
            var mappedArgument = MapTypeToCompilation(
                argument,
                compilation);
            if (mappedArgument == null)
            {
                return null;
            }

            arguments.Add(mappedArgument);
        }

        return definition.Construct(
            arguments.ToImmutable().ToArray());
    }

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType != null)
        {
            return GetMetadataName(type.ContainingType) +
                "+" +
                type.MetadataName;
        }

        var namespaceName =
            type.ContainingNamespace?.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName)
            ? type.MetadataName
            : namespaceName + "." + type.MetadataName;
    }

    private static ITypeSymbol? GetExpectedResourceType(AkburaSemanticModel semanticModel, AkburaResourceKeyCompletionContext context, CancellationToken cancellationToken)
    {
        var semanticRoot = semanticModel.SyntaxTree.GetRootSyntax();
        var semanticNode = semanticRoot.FindToken(
            context.Extension.OpenBrace.Span.End).Parent;
        while (semanticNode != null &&
               semanticNode is not MarkupExtensionSyntax)
        {
            semanticNode = semanticNode.Parent;
        }

        var semanticExtension =
            semanticNode as MarkupExtensionSyntax;
        if (semanticExtension == null)
        {
            return null;
        }

        MarkupExtensionPropertyArgumentSyntax? nestedArgument = null;
        MarkupExtensionSyntax? outerExtension = null;
        for (var node = semanticExtension.Parent; node != null; node = node.Parent)
        {
            if (nestedArgument == null &&
                node is MarkupExtensionPropertyArgumentSyntax argument)
            {
                nestedArgument = argument;
                continue;
            }

            if (nestedArgument != null &&
                node is MarkupExtensionSyntax outerCandidate)
            {
                outerExtension = outerCandidate;
                break;
            }

            if (node is MarkupAttributeSyntax)
            {
                break;
            }
        }

        if (nestedArgument != null &&
            outerExtension != null)
        {
            return semanticModel
                .GetMarkupExtensionArgumentValueTypeForCompletion(
                    outerExtension,
                    nestedArgument.Name.ToFullString().Trim(),
                    cancellationToken);
        }

        MarkupAttributeSyntax? attribute = null;
        for (var node = semanticExtension.Parent; node != null; node = node.Parent)
        {
            if (node is MarkupAttributeSyntax containingAttribute)
            {
                attribute = containingAttribute;
                break;
            }
        }

        if (attribute == null ||
            AkburaSemanticModel.GetContainingMarkupElement(attribute) is
                not { } element ||
            semanticModel.GetSymbolInfo(attribute).Symbol is not
                Akbura.Language.Symbols.IPropertySymbol property)
        {
            return null;
        }

        var contract = semanticModel.GetMarkupPropertyAssignmentContract(
            property,
            element);
        return contract.ContextualValueType ??
            contract.DeclaredType;
    }

    private static AkburaCompletionItem CreateResourceCompletionItem(ResourceCompletionCandidate candidate, AkburaResourceKeyCompletionContext context, int priority)
    {
        var types = candidate.Origins
            .OrderBy(static origin => origin.Kind)
            .Select(static origin => origin.ResourceType)
            .Where(static type => type != null)
            .Select(static type => type!.ToDisplayString(
                Microsoft.CodeAnalysis.SymbolDisplayFormat
                    .MinimallyQualifiedFormat))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        var origins = candidate.Origins
            .OrderBy(static origin => origin.Kind)
            .Select(static origin => origin.Description)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        var suffix = types.Length == 0
            ? "Unknown"
            : string.Join(" | ", types);
        var description = suffix + Environment.NewLine +
            string.Join(Environment.NewLine, origins);

        return new AkburaCompletionItem(
            candidate.Key,
            GetResourceInsertion(candidate.Key, context.Quote),
            AkburaCompletionKind.Resource,
            description,
            descriptionFactory: null,
            filterText: candidate.Key,
            sortText: $"{priority:D10}_{candidate.Key}",
            suffix: suffix,
            priority: priority);
    }

    private static string GetResourceInsertion(string key, char existingQuote)
    {
        if (existingQuote != '\0')
        {
            return EscapeResourceKey(key, existingQuote);
        }

        if (key.All(IsUnquotedResourceKeyCharacter))
        {
            return key;
        }

        return "\"" + EscapeResourceKey(key, '"') + "\"";
    }

    private static string EscapeResourceKey(string key, char quote)
    {
        StringBuilder? builder = null;
        for (var index = 0; index < key.Length; index++)
        {
            var character = key[index];
            if (character is not '\\' && character != quote)
            {
                builder?.Append(character);
                continue;
            }

            if (builder == null)
            {
                builder = new StringBuilder(key.Length + 4);
                builder.Append(key, 0, index);
            }

            builder.Append('\\');
            builder.Append(character);
        }

        return builder?.ToString() ?? key;
    }

    private static bool IsUnquotedResourceKeyCharacter(char character)
    {
        return char.IsLetterOrDigit(character) ||
            character is '_' or '-' or '.' or ':' or '/';
    }

    private readonly struct RankedResourceCandidate
    {
        public RankedResourceCandidate(ResourceCompletionCandidate candidate, int priority)
        {
            Candidate = candidate;
            Priority = priority;
        }

        public ResourceCompletionCandidate Candidate { get; }

        public int Priority { get; }
    }

    private sealed class RankedResourceCandidateComparer :
        IComparer<RankedResourceCandidate>
    {
        public static RankedResourceCandidateComparer Instance { get; } =
            new();

        public int Compare(RankedResourceCandidate left, RankedResourceCandidate right)
        {
            var priority = left.Priority.CompareTo(right.Priority);
            return priority != 0
                ? priority
                : StringComparer.Ordinal.Compare(
                    left.Candidate.Key,
                    right.Candidate.Key);
        }
    }
}
