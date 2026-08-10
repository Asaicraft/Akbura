using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.Workspaces;

internal sealed class AkburaCompletionService : IAkburaCompletionService
{
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
                ImmutableArray<AkburaCompletionItem>.Empty);
        }

        var semanticModel = semanticContext.Project.Compilation
            .GetSemanticModel(
                semanticContext.Document.SyntaxTree);
        var items = syntaxContext.Kind switch
        {
            AkburaCompletionContextKind.ComponentName =>
                GetComponentItems(
                    semanticModel,
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
            items);
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
            CancellationToken cancellationToken)
    {
        var items = new Dictionary<string, AkburaCompletionItem>(
            StringComparer.Ordinal);

        foreach (var candidate in
                 semanticModel.LookupMarkupComponents(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = candidate.Symbol;
            var description = symbol.ComponentType?.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat) ??
                symbol.AkburaComponent?.MetadataName ??
                symbol.MetadataName;
            if (!items.ContainsKey(candidate.DisplayName))
            {
                items.Add(
                    candidate.DisplayName,
                    new AkburaCompletionItem(
                        candidate.DisplayName,
                        candidate.DisplayName,
                        AkburaCompletionKind.Component,
                        description,
                        sortText: description));
            }
        }

        return items.Values
            .OrderBy(static item => item.SortText,
                StringComparer.Ordinal)
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
        if (string.IsNullOrWhiteSpace(componentName) ||
            !semanticModel.TryResolveMarkupComponentForCompletion(
                componentName!,
                out var target))
        {
            return ImmutableArray<AkburaCompletionItem>.Empty;
        }

        var existing = new HashSet<string>(
            context.ExistingAttributeNames,
            StringComparer.Ordinal);
        var items = new Dictionary<string, AkburaCompletionItem>(
            StringComparer.Ordinal);
        var ownerName = GetSimpleName(componentName!);

        if (!propertyElements && !existing.Contains("x.Name"))
        {
            items.Add(
                "x.Name",
                new AkburaCompletionItem(
                    "x.Name",
                    "x.Name",
                    AkburaCompletionKind.Property,
                    "Names this element in the current Akbura component."));
        }

        if (target.AkburaComponent != null)
        {
            foreach (var parameter in
                     target.AkburaComponent.Parameters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!parameter.ReceivesValueFromParent ||
                    existing.Contains(parameter.Name))
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
                    if (existing.Contains(command.Name))
                    {
                        continue;
                    }

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
                existing,
                propertyElements,
                cancellationToken);
        }

        return items.Values
            .OrderBy(static item => item.SortText,
                StringComparer.Ordinal)
            .ToImmutableArray();
    }

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
                displayName,
                propertyElements
                    ? AkburaCompletionKind.PropertyElement
                    : kind,
                typeDisplay.Length == 0
                    ? memberName
                    : typeDisplay + " " + memberName));
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

}
