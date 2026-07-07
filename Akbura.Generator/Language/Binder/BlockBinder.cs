using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Binder;

internal sealed class BlockBinder : Binder
{
    private ImmutableArray<ISymbol> _lazyDeclaredSymbols;

    public BlockBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        Declaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            DeclarationFacts.GetSyntax(declaration),
            flags | AkburaBinderFlags.InCSharpBlock)
    {
    }

    public override ImmutableArray<ISymbol> GetDeclaredSymbolsForScope(AkburaSyntax scopeDesignator)
    {
        if (!OwnsScope(scopeDesignator) ||
            Declaration == null)
        {
            return base.GetDeclaredSymbolsForScope(scopeDesignator);
        }

        return GetDeclaredSymbols();
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

        var symbols = GetDeclaredSymbolsForScope(DeclarationFacts.GetSyntax(Declaration));
        foreach (var symbol in symbols)
        {
            if (!string.Equals(symbol.Name, name, System.StringComparison.Ordinal) ||
                !IsVisibleAt(symbol, syntax))
            {
                continue;
            }

            result.SetSymbol(symbol);
            return;
        }
    }

    private ImmutableArray<ISymbol> GetDeclaredSymbols()
    {
        var symbols = _lazyDeclaredSymbols;
        if (!symbols.IsDefault)
        {
            return symbols;
        }

        symbols = CreateLocalSymbols();
        ImmutableInterlocked.InterlockedInitialize(ref _lazyDeclaredSymbols, symbols);
        return _lazyDeclaredSymbols;
    }

    private ImmutableArray<ISymbol> CreateLocalSymbols()
    {
        if (Declaration == null ||
            Declaration.Children.IsDefaultOrEmpty)
        {
            return ImmutableArray<ISymbol>.Empty;
        }

        var builder = ArrayBuilder<ISymbol>.GetInstance();
        foreach (var child in Declaration.Children)
        {
            if (child.Kind != DeclarationKind.CSharpStatement ||
                !DeclarationFacts.TryGetSyntax(child, out var childSyntax) ||
                childSyntax.Kind != AkburaSyntaxKind.CSharpStatementSyntax)
            {
                continue;
            }

            var statement = Unsafe.As<CSharpStatementSyntax>(childSyntax);
            AddLocalSymbols(builder, statement);
        }

        return builder.ToImmutableAndFree();
    }

    private void AddLocalSymbols(
        ArrayBuilder<ISymbol> builder,
        CSharpStatementSyntax statement)
    {
        var parsedStatement = ParseStatement(statement);
        if (parsedStatement is not CSharp.LocalDeclarationStatementSyntax)
        {
            return;
        }

        var probeBinder = new CSharpProbeBinder(SemanticModel, NextRequired, Flags);
        if (probeBinder.BindStatement(statement, parsedStatement) is not BoundLocalDeclarationStatement localDeclaration ||
            localDeclaration.Locals.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var local in localDeclaration.Locals)
        {
            builder.Add(new CSharpLocalSymbol(local, statement));
        }
    }

    private static CSharp.StatementSyntax ParseStatement(CSharpStatementSyntax statement)
    {
        return CSharpSyntaxFactory.ParseStatement(statement.Tokens.ToFullString());
    }

    private static bool IsVisibleAt(ISymbol symbol, AkburaSyntax syntax)
    {
        if (symbol is not CSharpLocalSymbol localSymbol)
        {
            return true;
        }

        return ReferenceEquals(localSymbol.DeclarationSyntax, syntax) ||
               localSymbol.DeclarationSyntax.Position < syntax.Position;
    }
}
