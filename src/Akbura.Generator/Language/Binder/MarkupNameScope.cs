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
    private readonly MarkupRootSyntax _root;
    private readonly MarkupTemplateContentResolver _templateContentResolver;
    private readonly ImmutableArray<Scope> _scopes;

    private MarkupNameScope(
        MarkupRootSyntax root,
        MarkupTemplateContentResolver templateContentResolver,
        ImmutableArray<Scope> scopes,
        ImmutableArray<MarkupNameDeclaration> declarations)
    {
        _root = root;
        _templateContentResolver = templateContentResolver;
        _scopes = scopes;
        Declarations = declarations;
    }

    public ImmutableArray<MarkupNameDeclaration> Declarations { get; }

    public static MarkupNameScope Create(
        MarkupRootSyntax root,
        MarkupTemplateContentResolver templateContentResolver)
    {
        using var declarations = ImmutableArrayBuilder<MarkupNameDeclaration>.Rent();
        var scopes = new Dictionary<AkburaSyntax, ScopeBuilder>();
        scopes.Add(root, new ScopeBuilder(root));

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
                var owner =
                    (AkburaSyntax?)templateContentResolver.GetLocalNameScopeOwner(element) ??
                    root;
                if (!scopes.TryGetValue(owner, out var scope))
                {
                    scope = new ScopeBuilder(owner);
                    scopes.Add(owner, scope);
                }

                var declaration = MarkupNameDeclaration.Create(
                    element,
                    attachedAttribute,
                    originalDeclaration: null);
                if (declaration.IsValid &&
                    scope.DeclarationsByName.TryGetValue(declaration.Name, out var originalDeclaration))
                {
                    declaration = MarkupNameDeclaration.Create(
                        element,
                        attachedAttribute,
                        originalDeclaration);
                }
                else if (declaration.IsValid)
                {
                    scope.DeclarationsByName.Add(declaration.Name, declaration);
                }

                scope.Declarations.Add(declaration);
                declarations.Add(declaration);
            }
        }

        using var scopeBuilder = ImmutableArrayBuilder<Scope>.Rent(scopes.Count);
        foreach (var scope in scopes.Values)
        {
            scopeBuilder.Add(scope.ToScope());
        }

        return new MarkupNameScope(
            root,
            templateContentResolver,
            scopeBuilder.ToImmutable(),
            declarations.ToImmutable());
    }

    public ImmutableArray<ISymbol> GetDeclaredSymbols(AkburaSemanticModel semanticModel)
    {
        return GetDeclaredSymbolsOwnedBy(semanticModel, _root);
    }

    internal ImmutableArray<ISymbol> GetDeclaredSymbols(
        AkburaSemanticModel semanticModel,
        AkburaSyntax syntax)
    {
        return GetDeclaredSymbolsOwnedBy(
            semanticModel,
            GetScopeOwner(syntax));
    }

    internal ImmutableArray<ISymbol> GetDeclaredSymbolsOwnedBy(
        AkburaSemanticModel semanticModel,
        AkburaSyntax owner)
    {
        foreach (var scope in _scopes)
        {
            if (ReferenceEquals(scope.Owner, owner))
            {
                return scope.GetDeclaredSymbols(semanticModel);
            }
        }

        return ImmutableArray<ISymbol>.Empty;
    }

    internal bool TryGetVisibleDeclaredSymbol(
        AkburaSemanticModel semanticModel,
        AkburaSyntax syntax,
        string name,
        out IMarkupNameSymbol symbol)
    {
        var owner = GetScopeOwner(syntax);

        while (true)
        {
            foreach (var candidate in GetDeclaredSymbolsOwnedBy(
                         semanticModel,
                         owner))
            {
                if (candidate is IMarkupNameSymbol nameSymbol &&
                    string.Equals(
                        nameSymbol.Name,
                        name,
                        StringComparison.Ordinal))
                {
                    symbol = nameSymbol;
                    return true;
                }
            }

            if (ReferenceEquals(owner, _root))
            {
                break;
            }

            owner = owner is MarkupElementSyntax element
                ? (AkburaSyntax?)_templateContentResolver
                      .GetLocalNameScopeOwner(element) ??
                  _root
                : _root;
        }

        symbol = null!;
        return false;
    }

    internal bool Owns(
        AkburaSyntax owner,
        AkburaSyntax syntax)
    {
        var scopeOwner = GetScopeOwner(syntax);
        return ReferenceEquals(owner, scopeOwner) ||
               owner is MarkupRootSyntax root &&
               ReferenceEquals(root.Element, scopeOwner);
    }

    private AkburaSyntax GetScopeOwner(AkburaSyntax syntax)
    {
        return (AkburaSyntax?)_templateContentResolver.GetLocalNameScopeOwner(syntax) ??
               _root;
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

    private sealed class Scope
    {
        private ImmutableArray<ISymbol> _lazySymbols;

        public Scope(
            AkburaSyntax owner,
            ImmutableArray<MarkupNameDeclaration> declarations)
        {
            Owner = owner;
            Declarations = declarations;
        }

        public AkburaSyntax Owner { get; }

        public ImmutableArray<MarkupNameDeclaration> Declarations { get; }

        public ImmutableArray<ISymbol> GetDeclaredSymbols(
            AkburaSemanticModel semanticModel)
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
    }

    private sealed class ScopeBuilder
    {
        public ScopeBuilder(AkburaSyntax owner)
        {
            Owner = owner;
        }

        public AkburaSyntax Owner { get; }

        public List<MarkupNameDeclaration> Declarations { get; } = [];

        public Dictionary<string, MarkupNameDeclaration> DeclarationsByName { get; } =
            new(StringComparer.Ordinal);

        public Scope ToScope()
        {
            return new Scope(Owner, Declarations.ToImmutableArray());
        }
    }
}
