using Akbura.Collections;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Workspaces.Navigation;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Completion;

internal sealed class AkcssCompletionService
{
    private const int MaximumCompletionItems = 50;
    private static readonly AkcssSymbolDisplayService s_symbolDisplay = new();
    private static readonly IComparer<AkburaCompletionItem> s_displayTextComparer =
        Comparer<AkburaCompletionItem>.Create(static (left, right) => StringComparer.Ordinal.Compare(left.DisplayText, right.DisplayText));

    private readonly AkcssValueCompletionService _valueCompletionService =
        new();

    public AkburaCompletionResult GetCompletions(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        AkcssSyntacticCompletionContext context,
        CancellationToken cancellationToken)
    {
        if (context.IsDefault)
        {
            return new AkburaCompletionResult(
                context.ApplicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty);
        }

        var semanticModel = semanticContext == null
            ? null
            : semanticContext.Project.Compilation.GetSemanticModel(
                semanticContext.Document.SyntaxTree);

        var items = context.Kind switch
        {
            AkcssCompletionContextKind.TopLevel =>
                GetTopLevelItems(context),

            AkcssCompletionContextKind.SelectorSnippet => GetSelectorItems(context),
            AkcssCompletionContextKind.IncludeName => GetIncludeItems(semanticModel, context, cancellationToken),
            AkcssCompletionContextKind.BaseTheme => GetBaseThemeItems(semanticModel, context, cancellationToken),
            AkcssCompletionContextKind.TemplateBindingProperty => GetTemplateBindingItems(semanticModel, context, cancellationToken),
            AkcssCompletionContextKind.TemplatePart => GetTemplatePartItems(semanticModel, context),
            AkcssCompletionContextKind.PseudoClass => GetPseudoClassItems(semanticModel, context),
            AkcssCompletionContextKind.TemplateTraversal => [new AkburaCompletionItem(
                "/template/", "/template/", AkburaCompletionKind.Keyword, "Selects an element within a control template.")],
            AkcssCompletionContextKind.TemplateMarkupBody => GetTemplateBodyItems(context),

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

            AkcssCompletionContextKind.AttachedPropertyExpression =>
                GetAttachedPropertyExpressionItems(
                    semanticModel,
                    context,
                    cancellationToken),

            AkcssCompletionContextKind.PropertyValue =>
                GetValueItems(
                    semanticModel,
                    context,
                    cancellationToken),

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
        AddKeyword(items, context.Prefix, "@theme", "@theme ", "Declares a control theme.", triggerCompletionAfterInsert: true);
        AddKeyword(items, context.Prefix, "@mixin", "@mixin ", "Declares a reusable configuration for a receiver type.", triggerCompletionAfterInsert: true);
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

        AddKeyword(items, context.Prefix, "@include", "@include ", "Applies a mixin.", triggerCompletionAfterInsert: true);
        if (context.OwnerKind == SyntaxKind.AkcssThemeDeclarationSyntax)
        {
            AddKeyword(items, context.Prefix, "@template", "@template {\n\n}", "Declares the theme's control template.", caretOffsetFromEnd: 2);
            AddKeyword(items, context.Prefix, "@based-on", "@based-on ;", "Selects the inherited theme.", caretOffsetFromEnd: 1, triggerCompletionAfterInsert: true);
        }
        else if (context.OwnerKind == SyntaxKind.AkcssMixinDeclarationSyntax)
        {
            AddKeyword(items, context.Prefix, "@template-bind", "@template-bind ;", "Binds properties when applied to an element in a template.",
                caretOffsetFromEnd: 1, triggerCompletionAfterInsert: true);
        }

        if (context.OwnerKind is SyntaxKind.AkcssStyleRuleSyntax or SyntaxKind.AkcssUtilityDeclarationSyntax &&
            !HasInterceptDirective(document, context.ContainingDeclarationSpan))
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
                         context.OwnerSpan,
                         context.Qualifier,
                         cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        }

        return items.ToImmutable();
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetAttachedPropertyExpressionItems(
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
                     .LookupReadableAkcssPropertiesForCompletion(
                         context.OwnerSpan,
                         context.Qualifier,
                         cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!candidate.IsAttached ||
                !MatchesPrefix(
                    candidate.InsertName,
                    context.Prefix))
            {
                continue;
            }

            items.Add(new AkburaCompletionItem(
                candidate.InsertName,
                candidate.InsertName,
                AkburaCompletionKind.Property,
                $"Attached property {candidate.DisplayName}: " +
                    candidate.TypeDisplay,
                descriptionFactory: null,
                filterText: candidate.InsertName,
                suffix: candidate.TypeDisplay + " \u00B7 " +
                    candidate.OwnerTypeDisplay,
                priority: 0));
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

        var currentLogicalName =
            (semanticContext.Document.SyntaxTree as AkcssSyntaxTree)?
                .LogicalName;
        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        foreach (var name in semanticContext.Project.Compilation
                     .GetAvailableAkcssModuleNames(cancellationToken)
                     .OrderBy(static name => name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(
                    name,
                    currentLogicalName,
                    StringComparison.Ordinal) ||
                !MatchesPrefix(name, context.Prefix))
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

    private ImmutableArray<AkburaCompletionItem> GetValueItems(
        AkburaSemanticModel? semanticModel,
        AkcssSyntacticCompletionContext context,
        CancellationToken cancellationToken)
    {
        return semanticModel == null
            ? GetFallbackValueItems(context)
            : _valueCompletionService.GetItems(
                semanticModel,
                context,
                cancellationToken);
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetFallbackValueItems(AkcssSyntacticCompletionContext context)
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

    private static ImmutableArray<AkburaCompletionItem> GetIncludeItems(
        AkburaSemanticModel? model, AkcssSyntacticCompletionContext context, CancellationToken cancellationToken)
    {
        if (model?.FindAkcssEditorSyntax(context.OwnerSpan, context.OwnerKind) is not AkcssIncludeSyntax site)
        {
            return [];
        }

        var receiver = model.GetAkcssEditorContext(site)?.Subject.Type.Symbol as ITypeSymbol;
        var mixins = model.LookupAkcssSourceDeclarations<AkcssMixinSymbol>(site, name: null);
        var ambiguous = PooledHashSet<string>.GetInstance();
        var items = ArrayBuilder<AkburaCompletionItem>.GetInstance();
        try
        {
            CollectAmbiguousNames(mixins, ambiguous, cancellationToken);
            foreach (var mixin in mixins)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ambiguous.Contains(mixin.Name) || !MatchesPrefix(mixin.Name, context.Prefix) ||
                    receiver is not null && mixin.TargetType.Symbol is ITypeSymbol required &&
                    !AkburaSemanticModel.IsAssignableTo(receiver, required))
                {
                    continue;
                }

                var name = mixin.DeclarationSyntax.Name.Identifier.Text;
                var addArguments = site.Arguments.OpenParen.IsMissing;
                var terminator = addArguments && site.Semicolon.IsMissing ? ";" : string.Empty;
                var insertion = addArguments ? name + "()" + terminator : name;
                var needsArguments = addArguments && !mixin.Parameters.IsEmpty;
                items.Add(new AkburaCompletionItem(name, insertion, AkburaCompletionKind.AkcssStyle,
                    s_symbolDisplay.FormatMixin(mixin), descriptionFactory: null,
                    suffix: mixin.Module?.MetadataName ?? mixin.DefinitionModel.SyntaxTree.FilePath,
                    caretOffsetFromEnd: needsArguments ? 1 + terminator.Length : 0,
                    triggerCompletionAfterInsert: needsArguments));
            }

            return items.ToImmutable();
        }
        finally
        {
            items.Free();
            ambiguous.Free();
        }
    }

    private static ImmutableArray<AkburaCompletionItem> GetBaseThemeItems(
        AkburaSemanticModel? model, AkcssSyntacticCompletionContext context, CancellationToken cancellationToken)
    {
        if (model?.FindAkcssEditorSyntax(context.OwnerSpan, context.OwnerKind) is not AkcssBasedOnDirectiveSyntax site)
        {
            return [];
        }

        var owner = model.GetAkcssEditorContext(site)?.Owner as AkcssThemeSymbol;
        var qualifier = context.Qualifier;
        if (qualifier.StartsWith("global::", StringComparison.Ordinal))
        {
            qualifier = qualifier["global::".Length..];
        }

        var themes = model.LookupAkcssSourceDeclarations<AkcssThemeSymbol>(site, name: null,
            moduleName: qualifier.Length == 0 ? null : qualifier);
        var ambiguous = PooledHashSet<string>.GetInstance();
        var items = ArrayBuilder<AkburaCompletionItem>.GetInstance();
        try
        {
            CollectAmbiguousNames(themes, ambiguous, cancellationToken);
            foreach (var theme in themes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ambiguous.Contains(theme.Name) || !MatchesPrefix(theme.Name, context.Prefix) ||
                    ReferenceEquals(theme, owner) ||
                    owner?.TargetType.Symbol is ITypeSymbol current && theme.TargetType.Symbol is ITypeSymbol baseType &&
                    !AkburaSemanticModel.IsAssignableTo(current, baseType))
                {
                    continue;
                }

                var name = theme.DeclarationSyntax.Name.Identifier.Text;
                items.Add(new AkburaCompletionItem(name, name, AkburaCompletionKind.AkcssStyle,
                    s_symbolDisplay.FormatTheme(theme), descriptionFactory: null, suffix: theme.Module?.MetadataName));
            }

            return items.ToImmutable();
        }
        finally
        {
            items.Free();
            ambiguous.Free();
        }
    }

    private static void CollectAmbiguousNames<TSymbol>(ImmutableArray<TSymbol> symbols, PooledHashSet<string> ambiguous,
        CancellationToken cancellationToken) where TSymbol : AkcssSourceDeclarationSymbol
    {
        if (symbols.Length < 2)
        {
            return;
        }

        var seen = PooledHashSet<string>.GetInstance();
        try
        {
            foreach (var symbol in symbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(symbol.Name))
                {
                    ambiguous.Add(symbol.Name);
                }
            }
        }
        finally
        {
            seen.Free();
        }
    }

    private static ImmutableArray<AkburaCompletionItem> GetTemplateBindingItems(
        AkburaSemanticModel? model, AkcssSyntacticCompletionContext context, CancellationToken cancellationToken)
    {
        if (model?.FindAkcssEditorSyntax(context.OwnerSpan, context.OwnerKind) is not AkcssTemplateBindSyntax site)
        {
            return [];
        }

        return model.LookupAkcssTemplateBindingProperties(site, context.Position, cancellationToken)
            .SelectAsArray(
                static (candidate, prefix) => MatchesPrefix(candidate.Destination.Name, prefix),
                static (candidate, _) => new AkburaCompletionItem(candidate.Destination.Name, candidate.Destination.Name, AkburaCompletionKind.Property,
                    (candidate.Source is { } source ? s_symbolDisplay.FormatProperty(source) : "Templated parent deferred") +
                    " → " + s_symbolDisplay.FormatProperty(candidate.Destination)),
                context.Prefix);
    }

    private static ImmutableArray<AkburaCompletionItem> GetTemplatePartItems(
        AkburaSemanticModel? model, AkcssSyntacticCompletionContext context)
    {
        if (model?.FindAkcssEditorSyntax(context.OwnerSpan, context.OwnerKind) is not AkcssNameSelectorSyntax site ||
            model.GetAkcssEditorContext(site)?.Selector is not { } selector)
        {
            return [];
        }

        var template = selector.Steps.FirstOrDefault(
            static (step, name) => HasSelectorPredicate(step.Target, name), site)?.SourceTemplate;
        if (template is null)
        {
            return [];
        }

        var items = ArrayBuilder<AkburaCompletionItem>.GetInstance();
        try
        {
            foreach (var part in template.NameScope.Declarations)
            {
                if (part.IsValid && MatchesPrefix(part.Name, context.Prefix) &&
                    ReferenceEquals(part, template.NameScope.FindUniqueRootDeclaration(part.Name)))
                {
                    items.Add(new AkburaCompletionItem(part.Name, part.IdentifierText, AkburaCompletionKind.Component,
                        "Named element in the control template."));
                }
            }

            items.Sort(s_displayTextComparer);
            return items.ToImmutable();
        }
        finally
        {
            items.Free();
        }
    }

    private static bool HasSelectorPredicate(AkcssSelectorCompound compound, AkburaSyntax site) =>
        compound.Predicates.Any(static (predicate, syntax) => ReferenceEquals(predicate.Syntax, syntax), site);

    private static ImmutableArray<AkburaCompletionItem> GetPseudoClassItems(
        AkburaSemanticModel? model, AkcssSyntacticCompletionContext context)
    {
        var names = PooledHashSet<string>.GetInstance();
        var items = ArrayBuilder<AkburaCompletionItem>.GetInstance();
        try
        {
            names.Add("disabled");
            names.Add("focus");
            names.Add("focus-visible");
            names.Add("focus-within");
            names.Add("pointerover");
            if (model?.FindAkcssEditorSyntax(context.OwnerSpan, context.OwnerKind) is { } site &&
                model.GetAkcssEditorContext(site)?.Selector is { } selector)
            {
                var compound = HasSelectorPredicate(selector.Initial, site) ? selector.Initial :
                    selector.Steps.FirstOrDefault(static (step, syntax) => HasSelectorPredicate(step.Target, syntax), site)?.Target;
                for (var type = compound?.Receiver.Type.Symbol as INamedTypeSymbol; type != null; type = type.BaseType)
                {
                    foreach (var attribute in type.GetAttributes())
                    {
                        if (attribute.AttributeClass?.ToDisplayString() != "Avalonia.Controls.Metadata.PseudoClassesAttribute")
                        {
                            continue;
                        }

                        foreach (var argument in attribute.ConstructorArguments)
                        {
                            if (argument.Kind != TypedConstantKind.Array || argument.IsNull)
                            {
                                continue;
                            }

                            foreach (var value in argument.Values)
                            {
                                if (value.Value is string name)
                                {
                                    names.Add(name.TrimStart(':'));
                                }
                            }
                        }
                    }
                }
            }

            foreach (var name in names)
            {
                if (MatchesPrefix(name, context.Prefix))
                {
                    items.Add(new AkburaCompletionItem(name, name, AkburaCompletionKind.Keyword, "Avalonia pseudo-class."));
                }
            }

            items.Sort(s_displayTextComparer);
            return items.ToImmutable();
        }
        finally
        {
            items.Free();
            names.Free();
        }
    }

    private static ImmutableArray<AkburaCompletionItem> GetTemplateBodyItems(AkcssSyntacticCompletionContext context)
    {
        using var items = ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        AddKeyword(items, context.Prefix, "@include", "@include ", "Configures the owning element with a mixin.", triggerCompletionAfterInsert: true);
        AddKeyword(items, context.Prefix, "@template-bind", "@template-bind ;", "Binds properties from the templated parent.",
            caretOffsetFromEnd: 1, triggerCompletionAfterInsert: true);
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
            AkcssCompletionContextKind.AttachedPropertyExpression or
            AkcssCompletionContextKind.PropertyValue or
            AkcssCompletionContextKind.ApplyItem or
            AkcssCompletionContextKind.AkcssModuleName or AkcssCompletionContextKind.IncludeName or
            AkcssCompletionContextKind.BaseTheme or AkcssCompletionContextKind.TemplatePart or
            AkcssCompletionContextKind.TemplateBindingProperty;
    }

}
