using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System;
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
        CSharpBlockSyntax blockSyntax,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration: null,
            blockSyntax,
            flags | AkburaBinderFlags.InCSharpBlock)
    {
        BlockSyntax = blockSyntax ?? throw new ArgumentNullException(nameof(blockSyntax));
    }

    public CSharpBlockSyntax BlockSyntax { get; }

    public override ImmutableArray<ISymbol> GetDeclaredSymbolsForScope(AkburaSyntax scopeDesignator)
    {
        if (!OwnsScope(scopeDesignator))
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
        var symbols = GetDeclaredSymbols();
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
        var builder = ArrayBuilder<ISymbol>.GetInstance();
        foreach (var member in BlockSyntax.Tokens)
        {
            if (member.Kind != AkburaSyntaxKind.CSharpStatementSyntax)
            {
                continue;
            }

            var statement = Unsafe.As<CSharpStatementSyntax>(member);
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
