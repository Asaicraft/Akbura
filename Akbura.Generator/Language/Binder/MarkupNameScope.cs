using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Binder;

internal sealed class MarkupNameScope
{
    private ImmutableArray<ISymbol> _lazySymbols;

    private MarkupNameScope(ImmutableArray<MarkupNameDeclaration> declarations)
    {
        Declarations = declarations;
    }

    public ImmutableArray<MarkupNameDeclaration> Declarations { get; }

    public static MarkupNameScope Create(
        MarkupRootSyntax root,
        MarkupTemplateContentResolver templateContentResolver)
    {
        using var declarations = ImmutableArrayBuilder<MarkupNameDeclaration>.Rent();
        var declarationsByName = new Dictionary<string, MarkupNameDeclaration>(StringComparer.Ordinal);

        foreach (var element in root.Element.DescendantNodesAndSelf().OfType<MarkupElementSyntax>())
        {
            if (element.StartTag == null)
            {
                continue;
            }

            foreach (var attribute in element.StartTag.Attributes)
            {
                if (!AkburaSemanticModel.IsMarkupNameDirective(attribute))
                {
                    continue;
                }

                var attachedAttribute = Unsafe.As<MarkupAttachedPropertyAttributeSyntax>(attribute);
                var declaration = MarkupNameDeclaration.Create(
                    element,
                    attachedAttribute,
                    originalDeclaration: null,
                    templateContentResolver.IsInsideTemplateContent(element));
                if (declaration.IsValid &&
                    declarationsByName.TryGetValue(declaration.Name, out var originalDeclaration))
                {
                    declaration = MarkupNameDeclaration.Create(
                        element,
                        attachedAttribute,
                        originalDeclaration,
                        isInsideTemplateContent: false);
                }
                else if (declaration.IsValid)
                {
                    declarationsByName.Add(declaration.Name, declaration);
                }

                declarations.Add(declaration);
            }
        }

        return new MarkupNameScope(declarations.ToImmutable());
    }

    public ImmutableArray<ISymbol> GetDeclaredSymbols(AkburaSemanticModel semanticModel)
    {
        if (!_lazySymbols.IsDefault)
        {
            return _lazySymbols;
        }

        if (Declarations.IsEmpty)
        {
            ImmutableInterlocked.InterlockedInitialize(
                ref _lazySymbols,
                ImmutableArray<ISymbol>.Empty);
            return _lazySymbols;
        }

        using var builder = ImmutableArrayBuilder<ISymbol>.Rent(Declarations.Length);
        foreach (var declaration in Declarations)
        {
            if (declaration.GetOrCreateSymbol(semanticModel) is { } symbol)
            {
                builder.Add(symbol);
            }
        }

        ImmutableInterlocked.InterlockedInitialize(
            ref _lazySymbols,
            builder.ToImmutable());
        return _lazySymbols;
    }

    public bool TryGetDeclaration(
        MarkupAttachedPropertyAttributeSyntax attribute,
        out MarkupNameDeclaration declaration)
    {
        foreach (var candidate in Declarations)
        {
            if (ReferenceEquals(candidate.Attribute, attribute))
            {
                declaration = candidate;
                return true;
            }
        }

        declaration = null!;
        return false;
    }
}
