using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class ComponentBinder : Binder
{
    private ImmutableArray<ISymbol> _lazyDeclaredSymbols;
    private ImmutableArray<ISymbol> _lazyMarkupNameSymbols;

    public ComponentBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        Declaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            DeclarationFacts.GetSyntax(declaration),
            flags | AkburaBinderFlags.InComponent)
    {
    }

    public string ComponentName => Declaration?.Name ?? string.Empty;

    public override ImmutableArray<ISymbol> GetDeclaredSymbolsForScope(AkburaSyntax scopeDesignator)
    {
        if (!OwnsScope(scopeDesignator) ||
            Declaration == null)
        {
            return base.GetDeclaredSymbolsForScope(scopeDesignator);
        }

        var declaredSymbols = GetDeclaredSymbols();
        var markupNameSymbols = GetDeclaredMarkupNameSymbols();
        if (declaredSymbols.IsEmpty)
        {
            return markupNameSymbols;
        }

        if (markupNameSymbols.IsEmpty)
        {
            return declaredSymbols;
        }

        using var builder = ImmutableArrayBuilder<ISymbol>.Rent(
            declaredSymbols.Length + markupNameSymbols.Length);
        foreach (var symbol in declaredSymbols)
        {
            builder.Add(symbol);
        }

        foreach (var symbol in markupNameSymbols)
        {
            builder.Add(symbol);
        }

        return builder.ToImmutable();
    }

    public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.AkburaDocumentSyntax or
                AkburaSyntaxKind.StateDeclarationSyntax or
                AkburaSyntaxKind.ParamDeclarationSyntax or
                AkburaSyntaxKind.InjectDeclarationSyntax or
                AkburaSyntaxKind.CommandDeclarationSyntax =>
                SemanticModel.GetMemberSemanticModel(syntax).BindSemanticSyntax(syntax),
            _ => base.BindSemanticSyntax(syntax),
        };
    }

    protected override void LookupSymbolsInSingleBinder(
        LookupResult result,
        string name,
        int arity,
        BinderLookupOptions options,
        Binder originalBinder,
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
        if (Declaration == null)
        {
            return;
        }

        foreach (var child in Declaration.Children)
        {
            if (!IsComponentMemberDeclaration(child.Kind) ||
                !string.Equals(child.Name, name, System.StringComparison.Ordinal) ||
                !IsVisibleAt(child, syntax) ||
                !SemanticModel.DeclarationSymbols.TryGetDeclaredSymbol(child, out var symbol))
            {
                continue;
            }

            result.SetSymbol(symbol);
            return;
        }

        var markupName = FindDeclaredSymbol(GetDeclaredMarkupNameSymbols(), name);
        if (markupName != null)
        {
            result.SetSymbol(markupName);
        }
    }

    private static bool IsComponentMemberDeclaration(DeclarationKind kind)
    {
        return kind is
            DeclarationKind.State or
            DeclarationKind.Parameter or
            DeclarationKind.InjectedService or
            DeclarationKind.Command;
    }

    private static bool IsVisibleAt(Declaration declaration, AkburaSyntax syntax)
    {
        if (declaration.Kind != DeclarationKind.State ||
            declaration is not SingleDeclaration singleDeclaration)
        {
            return true;
        }

        if (singleDeclaration.Syntax.Position >= syntax.Position)
        {
            return false;
        }

        for (var current = syntax; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, singleDeclaration.Syntax))
            {
                return false;
            }
        }

        return true;
    }

    internal ImmutableArray<ISymbol> GetDeclaredMarkupNameSymbols()
    {
        if (!_lazyMarkupNameSymbols.IsDefault)
        {
            return _lazyMarkupNameSymbols;
        }

        if (ScopeDesignator?.Kind != AkburaSyntaxKind.AkburaDocumentSyntax)
        {
            ImmutableInterlocked.InterlockedInitialize(
                ref _lazyMarkupNameSymbols,
                ImmutableArray<ISymbol>.Empty);
            return _lazyMarkupNameSymbols;
        }

        var document = Unsafe.As<AkburaDocumentSyntax>(ScopeDesignator);
        using var builder = ImmutableArrayBuilder<ISymbol>.Rent();
        foreach (var member in document.Members)
        {
            if (member.Kind != AkburaSyntaxKind.MarkupRootSyntax)
            {
                continue;
            }

            var scope = SemanticModel.BindingSession.GetMarkupNameScope(
                Unsafe.As<MarkupRootSyntax>(member));
            foreach (var symbol in scope.GetDeclaredSymbols(SemanticModel))
            {
                builder.Add(symbol);
            }
        }

        ImmutableInterlocked.InterlockedInitialize(
            ref _lazyMarkupNameSymbols,
            builder.ToImmutable());
        return _lazyMarkupNameSymbols;
    }

    private ImmutableArray<ISymbol> GetDeclaredSymbols()
    {
        var symbols = _lazyDeclaredSymbols;
        if (!symbols.IsDefault)
        {
            return symbols;
        }

        symbols = SemanticModel.DeclarationSymbols.GetDeclaredSymbols(
            Declaration!,
            DeclarationKind.State,
            DeclarationKind.Parameter,
            DeclarationKind.InjectedService,
            DeclarationKind.Command);

        ImmutableInterlocked.InterlockedInitialize(ref _lazyDeclaredSymbols, symbols);
        return _lazyDeclaredSymbols;
    }
}
