using Akbura.Collections;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Akbura.Workspaces.References;
using Microsoft.CodeAnalysis.Text;
using RoslynSymbolComparer = Microsoft.CodeAnalysis.SymbolEqualityComparer;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using NativeSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Workspaces.Navigation;

internal sealed partial class AkcssReferenceResolver
{
    internal ImmutableArray<AkcssResolvedReference> GetContextualReferences(
        AkburaSemanticModel model, AkburaSyntax syntax, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (syntax)
        {
            case AkcssThemeDeclarationSyntax theme:
                return Make(AkcssReferenceKind.ThemeDeclaration, theme.Name.Span, model.GetDeclaredSymbol(theme));
            case AkcssMixinDeclarationSyntax mixin:
                return Make(AkcssReferenceKind.MixinDeclaration, mixin.Name.Span, model.GetDeclaredSymbol(mixin));
            case AkcssMixinParameterSyntax parameter:
                return Make(AkcssReferenceKind.MixinParameter, parameter.Name.Span, model.GetDeclaredSymbol(parameter));
            case AkcssIncludeSyntax include:
                return model.LookupAkcssSourceDeclarations<AkcssMixinSymbol>(include, include.Name.Identifier.ValueText)
                    .SelectAsArray(static (mixin, span) =>
                        new AkcssResolvedReference(AkcssReferenceKind.MixinInclude, span, mixin, default), include.Name.Span);
            case AkcssBasedOnDirectiveSyntax basedOn:
                if (model.GetOperation(basedOn) is AkcssContextualOperation
                        { Data: AkcssBaseThemeData { SourceTheme: { } source } } &&
                    EmbeddedCSharpSyntaxFacts.TryGetExpression(basedOn.Expression, out var expression, out var host))
                {
                    while (expression is CSharp.ParenthesizedExpressionSyntax parenthesized)
                    {
                        expression = parenthesized.Expression;
                    }

                    var name = expression is CSharp.MemberAccessExpressionSyntax member ? member.Name : expression as CSharp.SimpleNameSyntax;
                    if (name is not null)
                    {
                        var span = new TextSpan(host.Start + name.Identifier.Span.Start, name.Identifier.Span.Length);
                        return Make(AkcssReferenceKind.ThemeReference, span, source);
                    }
                }

                return [];
            case AkcssNameSelectorSyntax name:
                return GetPartReferences(model, name, cancellationToken);
            case MarkupAttachedPropertyAttributeSyntax attribute
                when AkburaSemanticModel.IsMarkupNameDirective(attribute) && AkcssSemanticFacts.FindTemplate(attribute) is not null:
                return GetPartDeclaration(model, attribute);
            case AkcssTemplateBindSyntax binding:
                return GetTemplateBindingReferences(model, binding, cancellationToken);
            case CSharpExpressionSyntax embedded when AkcssSemanticFacts.IsEmbeddedExpression(embedded):
                return model.GetAkcssCSharpSymbolReferences(embedded)
                    .SelectAsArray(
                        static reference => reference.AkburaSymbol is not null || reference.CSharpDefinition.Symbol is not null,
                        static reference => new AkcssResolvedReference(AkcssReferenceKind.EmbeddedCSharp, reference.SourceSpan,
                            reference.AkburaSymbol, reference.CSharpDefinition));
            case CSharpTypeSyntax type when AkcssSemanticFacts.IsEmbeddedType(type) &&
                model.TryResolveAkcssTargetType(type, out var resolved):
                return [new AkcssResolvedReference(AkcssReferenceKind.PropertyOwnerType, type.Tokens.Span, null, resolved)];
            default:
                return [];
        }
    }

    private static ImmutableArray<AkcssResolvedReference> Make(AkcssReferenceKind kind, TextSpan span, NativeSymbol? symbol) =>
        symbol is null || span.IsEmpty ? [] : [new AkcssResolvedReference(kind, span, symbol, symbol.CSharpDefinition)];

    private static ImmutableArray<AkcssResolvedReference> GetPartDeclaration(
        AkburaSemanticModel model, MarkupAttachedPropertyAttributeSyntax attribute)
    {
        var context = model.GetAkcssEditorContext(attribute);
        if ((context?.ActiveTemplate ?? context?.SubjectTemplate) is not { } template ||
            !template.NameScope.TryGetDeclaration(attribute, out var declaration) ||
            declaration.GetOrCreateSymbol(model) is not Akbura.Language.Symbols.Symbol symbol ||
            !CSharpProbeBinder.TryGetDeclarationSpan(symbol, out var span))
        {
            return [];
        }

        return Make(AkcssReferenceKind.TemplatePartDeclaration, span, symbol);
    }

    private static ImmutableArray<AkcssResolvedReference> GetPartReferences(
        AkburaSemanticModel model, AkcssNameSelectorSyntax name, CancellationToken cancellationToken)
    {
        var context = model.GetAkcssEditorContext(name);
        if (context is null)
        {
            return [];
        }

        if (!context.IsMixinDefinition || AkcssSemanticFacts.FindOwner(name) is not AkcssMixinDeclarationSyntax)
        {
            return TryGetPartReference(context, name, includeUnresolved: false, out var reference) ? [reference] : [];
        }

        var rule = name.FirstAncestorOrSelf<AkcssNestedStyleRuleSyntax>();
        if (rule is null)
        {
            return [];
        }

        var result = ArrayBuilder<AkcssResolvedReference>.GetInstance();
        try
        {
            VisitBoundInclusions(model.Compilation, rule, cancellationToken, (result, name), static (instance, state) =>
            {
                if (instance.Context is { } expanded &&
                    TryGetPartReference(expanded, state.name, includeUnresolved: true, out var reference))
                {
                    state.result.Add(reference);
                }
            });
            return result.ToImmutable();
        }
        finally
        {
            result.Free();
        }
    }

    private static bool TryGetPartReference(AkcssBindingContext binding, AkcssNameSelectorSyntax name, bool includeUnresolved,
        out AkcssResolvedReference reference)
    {
        reference = default;
        if (binding.Selector is not { } selector)
        {
            return false;
        }

        foreach (var step in selector.Steps)
        {
            var predicate = step.Target.Predicates.FirstOrDefault(
                static (candidate, syntax) => ReferenceEquals(candidate.Syntax, syntax), name);
            if (predicate is null)
            {
                continue;
            }

            var symbol = step.SourceTemplate is { } template
                ? predicate.SourcePart?.GetOrCreateSymbol(template.Theme.DefinitionModel) : null;
            if (symbol is null && !(includeUnresolved && step.Relationship == AkcssSelectorRelationship.Template))
            {
                return false;
            }

            reference = new AkcssResolvedReference(AkcssReferenceKind.TemplatePartReference, name.Name.Span, symbol, default, binding);
            return true;
        }

        return false;
    }

    private static ImmutableArray<AkcssResolvedReference> GetTemplateBindingReferences(
        AkburaSemanticModel model, AkcssTemplateBindSyntax directive, CancellationToken cancellationToken)
    {
        var site = directive.Parent ?? directive;
        var definition = model.GetOperation(site) as AkcssContextualOperation;
        var result = ArrayBuilder<AkcssResolvedReference>.GetInstance();
        try
        {
            if (definition?.Data is AkcssTemplateBindingData { IsDeferred: true })
            {
                VisitBoundInclusions(model.Compilation, site, cancellationToken, result, AddBinding);
            }

            if (result.Count == 0 && definition is not null)
            {
                AddBinding(definition, result);
            }

            return result.ToImmutable();
        }
        finally
        {
            result.Free();
        }

        static void AddBinding(AkcssContextualOperation operation, ArrayBuilder<AkcssResolvedReference> result)
        {
            if (operation.Data is not AkcssTemplateBindingData bindings)
            {
                return;
            }

            foreach (var property in bindings.Properties)
            {
                if (property.Destination is { } destination)
                {
                    result.Add(new AkcssResolvedReference(AkcssReferenceKind.TemplateBindingDestination,
                        property.Syntax.Span, destination, destination.WriteDefinition, operation.Context, property));
                }

                if (property.Source is { } source)
                {
                    result.Add(new AkcssResolvedReference(AkcssReferenceKind.TemplateBindingSource,
                        property.Syntax.Span, source, source.ReadDefinition, operation.Context, property));
                }
            }
        }
    }

    private static void VisitBoundInclusions<TState>(AkburaCompilation compilation, AkburaSyntax syntax,
        CancellationToken cancellationToken, TState state, Action<AkcssContextualOperation, TState> visit)
    {
        var pending = ArrayBuilder<IOperation>.GetInstance();
        try
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                VisitIncludes(tree);
            }

            foreach (var tree in compilation.AkcssSyntaxTrees)
            {
                VisitIncludes(tree);
            }
        }
        finally
        {
            pending.Free();
        }

        void VisitIncludes(AkburaSyntaxTree tree)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRootSyntax().DescendantNodes())
            {
                if (node is not AkcssIncludeSyntax site)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (AkcssSemanticFacts.FindOwner(site) is AkcssMixinDeclarationSyntax ||
                    model.GetOperation(site.Parent ?? site) is not AkcssContextualOperation include)
                {
                    continue;
                }

                pending.Push(include);
                while (pending.TryPop(out var operation))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (operation is AkcssContextualOperation contextual && ReferenceEquals(operation.Syntax, syntax))
                    {
                        visit(contextual, state);
                    }

                    var children = operation.Children;
                    for (var i = children.Length - 1; i >= 0; i--)
                    {
                        pending.Push(children[i]);
                    }
                }
            }
        }
    }

    private static bool TrySelectReference(AkburaDocumentContext context, ImmutableArray<AkcssResolvedReference> candidates,
        int position, out AkcssResolvedReference reference)
    {
        reference = default;
        AkburaSymbolKey? key = null;
        var hasDestination = candidates.Any(static (candidate, caret) =>
            candidate.Kind == AkcssReferenceKind.TemplateBindingDestination && candidate.SourceSpan.Contains(caret), position);
        foreach (var candidate in candidates)
        {
            if (!candidate.SourceSpan.Contains(position) || hasDestination && candidate.Kind == AkcssReferenceKind.TemplateBindingSource)
            {
                continue;
            }

            var identity = candidate.Symbol is { } native ? AkburaSymbolKeyFactory.Create(context, native) :
                candidate.CSharpDefinition.Symbol is { } csharp ? AkburaSymbolKeyFactory.Create(context, csharp) : (AkburaSymbolKey?)null;
            if (identity is null || key is not null && key != identity)
            {
                reference = default;
                return false;
            }

            if (key is null)
            {
                key = identity;
                reference = candidate;
            }
            else if (reference.TemplateBinding is { } previous && candidate.TemplateBinding is { } current &&
                (!RoslynSymbolComparer.Default.Equals(previous.Source?.ReadDefinition.Symbol, current.Source?.ReadDefinition.Symbol) ||
                 !RoslynSymbolComparer.Default.Equals(reference.Context?.ActiveTemplate?.TemplatedParentType.Symbol,
                     candidate.Context?.ActiveTemplate?.TemplatedParentType.Symbol)))
            {
                reference = new AkcssResolvedReference(reference.Kind, reference.SourceSpan, reference.Symbol,
                    reference.CSharpDefinition, reference.Context, previous, hasAmbiguousTemplateSource: true);
            }
        }

        return key is not null;
    }
}
