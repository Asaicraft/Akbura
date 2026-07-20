using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class ComponentBinder : Binder
{
    private ImmutableArray<ISymbol> _lazyDeclaredSymbols;

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

        return GetDeclaredSymbols();
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
