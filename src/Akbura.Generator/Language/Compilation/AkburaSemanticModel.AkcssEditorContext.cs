using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    internal AkcssBindingContext? GetAkcssEditorContext(AkburaSyntax site)
    {
        var declaration = AkcssSemanticFacts.FindOwner(site);
        return declaration is not null &&
            GetDeclarationSymbolInfo(declaration).Symbol is IAkcssDeclarationOwner owner &&
            GetBinder(declaration, BinderUsage.Akcss) is AkcssStyleBinder binder
                ? binder.GetEditorContext(owner, site)
                : null;
    }

    internal AkburaSyntax? FindAkcssEditorSite(TextSpan span)
    {
        foreach (var node in SyntaxTree.GetRootSyntax().DescendantNodesAtPosition(span.Start))
        {
            if (node.FullSpan == span && (AkcssSemanticFacts.IsSourceDeclaration(node) ||
                node is AkcssStyleRuleSyntax or AkcssUtilityDeclarationSyntax or AkcssNestedStyleRuleSyntax or
                    MarkupElementSyntax or AkcssIfDirectiveSyntax or AkcssPseudoBlockSyntax))
            {
                return node;
            }
        }

        return null;
    }

    internal AkburaSyntax? FindAkcssEditorSyntax(TextSpan span, SyntaxKind kind)
    {
        foreach (var node in SyntaxTree.GetRootSyntax().DescendantNodesAtPosition(span.Start))
        {
            if (node.Kind == kind && node.FullSpan == span)
            {
                return node;
            }
        }

        return null;
    }

    internal ImmutableArray<AkcssTemplateBindingCompletionCandidate> LookupAkcssTemplateBindingProperties(
        AkcssTemplateBindSyntax directive, int position, CancellationToken cancellationToken)
    {
        if (GetAkcssEditorContext(directive) is not { PropertyReceiver.Symbol: INamedTypeSymbol receiver } context ||
            context.OwningElement is null && !context.IsMixinDefinition)
        {
            return [];
        }

        var parent = context.ActiveTemplate?.TemplatedParentType.Symbol as INamedTypeSymbol;
        if (parent is null && !context.IsMixinDefinition)
        {
            return [];
        }

        var names = PooledHashSet<string>.GetInstance();
        var excluded = PooledHashSet<string>.GetInstance();
        var sortedNames = ArrayBuilder<string>.GetInstance();
        var items = ArrayBuilder<AkcssTemplateBindingCompletionCandidate>.GetInstance();
        Dictionary<RoslynSymbol, AkburaSyntax>? configured = null;
        try
        {
            AddAkcssCompletionPropertyNames(receiver, names);
            foreach (var entry in directive.PropertyNames)
            {
                if (position < entry.Span.Start || position > entry.Span.End)
                {
                    excluded.Add(entry.ToFullString().Trim());
                }
            }

            if (context.OwningElement is { } element &&
                GetBinder(context.Definition, BinderUsage.Akcss) is AkcssStyleBinder binder)
            {
                configured = AkcssStyleBinder.RentAkcssConfigurationMap();
                binder.CollectOtherConfiguredAkcssProperties(context, element, directive, configured);
            }

            foreach (var name in names)
            {
                if (!excluded.Contains(name))
                {
                    sortedNames.Add(name);
                }
            }

            sortedNames.Sort(StringComparer.Ordinal);
            foreach (var name in sortedNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryGetAkcssPropertyReference(name, receiver, context.Owner, out var destination) ||
                    destination.AvaloniaPropertyDefinition.Symbol is not { } identity ||
                    configured?.ContainsKey(identity) == true)
                {
                    continue;
                }

                Symbols.IPropertySymbol? source = null;
                if (parent is not null && !TryGetAkcssPropertyReference(name, parent, context.Owner, out source))
                {
                    continue;
                }

                items.Add(new AkcssTemplateBindingCompletionCandidate(source, destination));
            }

            return items.ToImmutable();
        }
        finally
        {
            if (configured is not null)
            {
                AkcssStyleBinder.FreeAkcssConfigurationMap(configured);
            }

            items.Free();
            sortedNames.Free();
            excluded.Free();
            names.Free();
        }
    }

    internal ITypeSymbol? GetAkcssExpressionExpectedType(CSharpExpressionSyntax expression, AkcssBindingContext context)
    {
        switch (expression.Parent)
        {
            case AkcssAssignmentSyntax assignment:
                using (var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent())
                {
                    return ResolveAkcssProperty(assignment, context, diagnostics, validateReceiver: true)?.Type.Symbol as ITypeSymbol;
                }
            case AkcssIfDirectiveSyntax:
                return Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Boolean);
            case AkcssBasedOnDirectiveSyntax:
                return Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Styling.ControlTheme");
            case AkcssMixinParameterDefaultSyntax { Parent: AkcssMixinParameterSyntax parameter }:
                return (GetDeclarationSymbolInfo(parameter).Symbol as AkcssMixinParameterSymbol)?.Type.Symbol as ITypeSymbol;
            case AkcssMixinArgumentListSyntax { Parent: AkcssIncludeSyntax include } list:
                return GetAkcssIncludeParameterType(include, list.Arguments.IndexOf(expression));
        }

        return null;
    }

    private ITypeSymbol? GetAkcssIncludeParameterType(AkcssIncludeSyntax include, int index)
    {
        var mixins = LookupAkcssSourceDeclarations<AkcssMixinSymbol>(include, include.Name.Identifier.ValueText);
        return mixins.Length == 1 && (uint)index < (uint)mixins[0].Parameters.Length
            ? mixins[0].Parameters[index].Type.Symbol as ITypeSymbol
            : null;
    }
}

internal readonly record struct AkcssTemplateBindingCompletionCandidate(Symbols.IPropertySymbol? Source, Symbols.IPropertySymbol Destination);
