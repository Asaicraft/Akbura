using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.Workspaces;

internal sealed class AkburaCompletionService : IAkburaCompletionService
{
    private const int MaximumCompletionItems = 50;

    private static readonly ConditionalWeakTable<
        AkburaSemanticModel,
        SemanticModelCompletionCache> CompletionCaches = new();

    public AkburaCompletionResult GetCompletions(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var syntaxContext = document.GetCompletionContext(
            position,
            cancellationToken);
        if (syntaxContext.IsDefault)
        {
            return new AkburaCompletionResult(
                syntaxContext.ApplicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty);
        }

        if (syntaxContext.Kind ==
                AkburaCompletionContextKind.ClosingComponentName)
        {
            return CreateClosingTagResult(syntaxContext);
        }

        if (semanticContext == null ||
            semanticContext.Document.SyntaxTree.Kind ==
                SyntaxTreeKind.Akcss)
        {
            return new AkburaCompletionResult(
                syntaxContext.ApplicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty,
                isIncomplete: IsSemanticCompletionContext(
                    syntaxContext.Kind));
        }

        var semanticModel = semanticContext.Project.Compilation
            .GetSemanticModel(
                semanticContext.Document.SyntaxTree);
        var items = syntaxContext.Kind switch
        {
            AkburaCompletionContextKind.ComponentName =>
                GetComponentItems(
                    semanticModel,
                    syntaxContext.Prefix,
                    cancellationToken),

            AkburaCompletionContextKind.AttributeName =>
                GetMemberItems(
                    semanticModel,
                    syntaxContext,
                    propertyElements: false,
                    cancellationToken),

            AkburaCompletionContextKind.PropertyElementName =>
                GetMemberItems(
                    semanticModel,
                    syntaxContext,
                    propertyElements: true,
                    cancellationToken),

            _ => ImmutableArray<AkburaCompletionItem>.Empty,
        };

        return new AkburaCompletionResult(
            syntaxContext.ApplicableSpan,
            items,
            isIncomplete: IsSemanticCompletionContext(
                syntaxContext.Kind));
    }

    private static AkburaCompletionResult CreateClosingTagResult(
        AkburaSyntacticCompletionContext context)
    {
        if (string.IsNullOrWhiteSpace(
                context.ParentComponentName))
        {
            return new AkburaCompletionResult(
                context.ApplicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty);
        }

        var name = context.ParentComponentName!;
        if (!MatchesPrefix(name, context.Prefix))
        {
            return new AkburaCompletionResult(
                context.ApplicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty);
        }

        return new AkburaCompletionResult(
            context.ApplicableSpan,
            ImmutableArray.Create(
                new AkburaCompletionItem(
                    name,
                    name,
                    AkburaCompletionKind.ClosingTag,
                    $"Close '{name}'.")));
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetComponentItems(
            AkburaSemanticModel semanticModel,
            string prefix,
            CancellationToken cancellationToken)
    {
        var items = new Dictionary<string, AkburaCompletionItem>(
            StringComparer.Ordinal);

        foreach (var candidate in
                 semanticModel.LookupMarkupComponents(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesPrefix(candidate.DisplayName, prefix) ||
                items.ContainsKey(candidate.DisplayName))
            {
                continue;
            }

            var priority = GetComponentPriority(candidate);
            var suffix = GetComponentSuffix(candidate);
            items.Add(
                candidate.DisplayName,
                new AkburaCompletionItem(
                    candidate.DisplayName,
                    candidate.DisplayName,
                    AkburaCompletionKind.Component,
                    description: string.Empty,
                    descriptionFactory: () =>
                        candidate.ComponentType?.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat) ??
                        candidate.MetadataName,
                    sortText:
                        $"{priority:D2}_{candidate.DisplayName}",
                    suffix: suffix,
                    priority: priority));
        }

        return items.Values
            .OrderBy(static item => item.SortText,
                StringComparer.Ordinal)
            .Take(MaximumCompletionItems)
            .ToImmutableArray();
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetMemberItems(
            AkburaSemanticModel semanticModel,
            AkburaSyntacticCompletionContext context,
            bool propertyElements,
            CancellationToken cancellationToken)
    {
        var componentName = propertyElements
            ? context.ParentComponentName
            : context.ComponentName;
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return ImmutableArray<AkburaCompletionItem>.Empty;
        }

        var existing = new HashSet<string>(
            context.ExistingAttributeNames,
            StringComparer.Ordinal);
        var cache = CompletionCaches.GetValue(
            semanticModel,
            static _ => new SemanticModelCompletionCache());
        var catalog = cache.GetOrCreate(
            componentName!,
            propertyElements,
            () => CreateMemberCatalog(
                semanticModel,
                componentName!,
                propertyElements,
                cancellationToken));

        return catalog
            .Where(candidate =>
                !existing.Contains(candidate.MemberName) &&
                MatchesPrefix(
                    candidate.Item.DisplayText,
                    context.Prefix))
            .Select(static candidate => candidate.Item)
            .OrderBy(static item => item.SortText,
                StringComparer.Ordinal)
            .Take(MaximumCompletionItems)
            .ToImmutableArray();
    }

    private static ImmutableArray<CompletionMemberCandidate>
        CreateMemberCatalog(
            AkburaSemanticModel semanticModel,
            string componentName,
            bool propertyElements,
            CancellationToken cancellationToken)
    {
        if (!semanticModel.TryResolveMarkupComponentForCompletion(
                componentName,
                out var target))
        {
            return ImmutableArray<CompletionMemberCandidate>.Empty;
        }

        var items = new Dictionary<string, AkburaCompletionItem>(
            StringComparer.Ordinal);
        var ownerName = GetSimpleName(componentName);

        if (!propertyElements)
        {
            items.Add(
                "x.Name",
                new AkburaCompletionItem(
                    "x.Name",
                    "x.Name=\"\"",
                    AkburaCompletionKind.Property,
                    "Names this element in the current Akbura component.",
                    descriptionFactory: null,
                    caretOffsetFromEnd: 1));
        }

        if (target.AkburaComponent != null)
        {
            foreach (var parameter in
                     target.AkburaComponent.Parameters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!parameter.ReceivesValueFromParent)
                {
                    continue;
                }

                AddMemberItem(
                    items,
                    ownerName,
                    parameter.Name,
                    AkburaCompletionKind.Parameter,
                    parameter.Type.ToDisplayString(
                        SymbolDisplayFormat.MinimallyQualifiedFormat),
                    propertyElements);
            }

            if (!propertyElements)
            {
                foreach (var command in
                         target.AkburaComponent.Commands)
                {
                    AddMemberItem(
                        items,
                        ownerName,
                        command.Name,
                        AkburaCompletionKind.Command,
                        command.ToDisplayString(),
                        propertyElements: false);
                }
            }
        }

        if (target.ComponentType != null)
        {
            AddClrMembers(
                items,
                ownerName,
                target.ComponentType,
                EmptyMemberNames,
                propertyElements,
                cancellationToken);
        }

        return items.Values
            .OrderBy(static item => item.SortText,
                StringComparer.Ordinal)
            .Select(item => new CompletionMemberCandidate(
                GetMemberName(item.DisplayText, propertyElements),
                item))
            .ToImmutableArray();
    }

    private static readonly HashSet<string> EmptyMemberNames =
        new(StringComparer.Ordinal);

    private static void AddClrMembers(
        Dictionary<string, AkburaCompletionItem> items,
        string ownerName,
        INamedTypeSymbol componentType,
        HashSet<string> existing,
        bool propertyElements,
        CancellationToken cancellationToken)
    {
        var visitedTypes = new HashSet<INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        for (var current = componentType;
             current != null;
             current = current.BaseType)
        {
            AddClrMembersFromType(
                items,
                ownerName,
                current,
                existing,
                propertyElements,
                cancellationToken);
            visitedTypes.Add(current);
        }

        foreach (var @interface in componentType.AllInterfaces)
        {
            if (visitedTypes.Add(@interface))
            {
                AddClrMembersFromType(
                    items,
                    ownerName,
                    @interface,
                    existing,
                    propertyElements,
                    cancellationToken);
            }
        }
    }

    private static void AddClrMembersFromType(
        Dictionary<string, AkburaCompletionItem> items,
        string ownerName,
        INamedTypeSymbol type,
        HashSet<string> existing,
        bool propertyElements,
        CancellationToken cancellationToken)
    {
        foreach (var member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (member)
            {
                case RoslynPropertySymbol property
                    when !property.IsStatic &&
                         !property.IsIndexer &&
                         property.DeclaredAccessibility ==
                             Accessibility.Public &&
                         !existing.Contains(property.Name):
                    AddMemberItem(
                        items,
                        ownerName,
                        property.Name,
                        AkburaCompletionKind.Property,
                        property.Type.ToDisplayString(
                            SymbolDisplayFormat.MinimallyQualifiedFormat),
                        propertyElements);
                    break;

                case IEventSymbol @event
                    when !propertyElements &&
                         !@event.IsStatic &&
                         @event.DeclaredAccessibility ==
                             Accessibility.Public &&
                         !existing.Contains(@event.Name):
                    AddMemberItem(
                        items,
                        ownerName,
                        @event.Name,
                        AkburaCompletionKind.Event,
                        @event.Type.ToDisplayString(
                            SymbolDisplayFormat.MinimallyQualifiedFormat),
                        propertyElements: false);
                    break;

                case IFieldSymbol field
                    when field.IsStatic &&
                         field.DeclaredAccessibility ==
                             Accessibility.Public &&
                         field.Name.EndsWith(
                             "Property",
                             StringComparison.Ordinal):
                    var propertyName = field.Name[..^"Property".Length];
                    if (!existing.Contains(propertyName))
                    {
                        AddMemberItem(
                            items,
                            ownerName,
                            propertyName,
                            AkburaCompletionKind.Property,
                            field.Type.ToDisplayString(
                                SymbolDisplayFormat.MinimallyQualifiedFormat),
                            propertyElements);
                    }

                    break;
            }
        }
    }

    private static void AddMemberItem(
        Dictionary<string, AkburaCompletionItem> items,
        string ownerName,
        string memberName,
        AkburaCompletionKind kind,
        string typeDisplay,
        bool propertyElements)
    {
        var displayName = propertyElements
            ? ownerName + "." + memberName
            : memberName;
        if (items.ContainsKey(displayName))
        {
            return;
        }

        items.Add(
            displayName,
            new AkburaCompletionItem(
                displayName,
                propertyElements
                    ? displayName
                    : displayName + "=\"\"",
                propertyElements
                    ? AkburaCompletionKind.PropertyElement
                    : kind,
                typeDisplay.Length == 0
                    ? memberName
                    : typeDisplay + " " + memberName,
                descriptionFactory: null,
                suffix: typeDisplay,
                caretOffsetFromEnd: propertyElements ? 0 : 1));
    }

    private static bool IsSemanticCompletionContext(
        AkburaCompletionContextKind kind)
    {
        return kind is
            AkburaCompletionContextKind.ComponentName or
            AkburaCompletionContextKind.AttributeName or
            AkburaCompletionContextKind.PropertyElementName;
    }

    private static bool MatchesPrefix(
        string value,
        string prefix)
    {
        return prefix.Length == 0 ||
            value.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase);
    }

    private static int GetComponentPriority(
        MarkupComponentLookupCandidate candidate)
    {
        if (candidate.IsAkburaComponent)
        {
            return 0;
        }

        var metadataName = candidate.ComponentType?.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat) ??
            candidate.MetadataName;
        if (metadataName.StartsWith(
                "global::Avalonia.Controls.",
                StringComparison.Ordinal))
        {
            return 10;
        }

        return metadataName.StartsWith(
            "global::Avalonia.",
            StringComparison.Ordinal)
                ? 20
                : 90;
    }

    private static string GetComponentSuffix(
        MarkupComponentLookupCandidate candidate)
    {
        if (candidate.IsAkburaComponent)
        {
            return "Akbura component";
        }

        return candidate.ComponentType?
                .ContainingNamespace
                .ToDisplayString() ??
            string.Empty;
    }

    private static string GetSimpleName(string componentName)
    {
        var name = componentName;
        var aliasSeparator = name.LastIndexOf("::", StringComparison.Ordinal);
        if (aliasSeparator >= 0)
        {
            name = name[(aliasSeparator + 2)..];
        }

        var namespaceSeparator = name.LastIndexOf('.');
        return namespaceSeparator < 0
            ? name
            : name[(namespaceSeparator + 1)..];
    }

    private static string GetMemberName(
        string displayName,
        bool propertyElements)
    {
        if (!propertyElements)
        {
            return displayName;
        }

        var separator = displayName.LastIndexOf('.');
        return separator < 0
            ? displayName
            : displayName[(separator + 1)..];
    }

    private readonly struct CompletionMemberCandidate
    {
        public CompletionMemberCandidate(
            string memberName,
            AkburaCompletionItem item)
        {
            MemberName = memberName;
            Item = item;
        }

        public string MemberName { get; }

        public AkburaCompletionItem Item { get; }
    }

    private sealed class SemanticModelCompletionCache
    {
        private readonly object _gate = new();
        private readonly Dictionary<string,
            ImmutableArray<CompletionMemberCandidate>> _catalogs =
            new(StringComparer.Ordinal);

        public ImmutableArray<CompletionMemberCandidate> GetOrCreate(
            string componentName,
            bool propertyElements,
            Func<ImmutableArray<CompletionMemberCandidate>> factory)
        {
            var key = (propertyElements ? "P\0" : "A\0") +
                componentName;
            lock (_gate)
            {
                if (_catalogs.TryGetValue(key, out var catalog))
                {
                    return catalog;
                }
            }

            var created = factory();
            lock (_gate)
            {
                if (_catalogs.TryGetValue(key, out var catalog))
                {
                    return catalog;
                }

                _catalogs.Add(key, created);
                return created;
            }
        }
    }

}
