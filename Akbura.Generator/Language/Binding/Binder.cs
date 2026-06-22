using Akbura.Language.Declarations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.Binding;

internal abstract class Binder
{
    protected Binder(
        AkburaSemanticModel semanticModel,
        Binder? next,
        AkburaDeclaration? declaration,
        AkburaSyntax? scopeDesignator,
        AkburaBinderFlags flags)
    {
        SemanticModel = semanticModel;
        Compilation = semanticModel.Compilation;
        Next = next;
        Declaration = declaration;
        ScopeDesignator = scopeDesignator;
        Flags = flags;
    }

    public AkburaSemanticModel SemanticModel { get; }

    public AkburaCompilation Compilation { get; }

    public Binder? Next { get; }

    public Binder NextRequired =>
        Next ?? throw new InvalidOperationException($"{GetType().Name} does not have a next binder.");

    public AkburaDeclaration? Declaration { get; }

    public AkburaSyntax? ScopeDesignator { get; }

    public AkburaBinderFlags Flags { get; }

    public virtual string ScopeKey =>
        Declaration == null
            ? GetType().Name
            : $"{GetType().Name}:{Declaration.Kind}:{Declaration.Name}:{ScopeDesignator?.FullSpan.ToString() ?? Declaration.Syntax.FullSpan.ToString()}";

    public virtual AkburaSymbolInfo LookupSymbol(AkburaSyntax syntax)
    {
        return Next?.LookupSymbol(syntax) ??
               AkburaSymbolInfo.None(Symbols.CandidateReason.UnsupportedSyntax);
    }

    public virtual AkburaSymbolInfo LookupSymbol(
        string name,
        BinderLookupOptions options,
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
        var result = LookupSymbolInSingleBinder(name, options, syntax, diagnostics);
        if (result.Symbol != null ||
            result.CandidateSymbols.Length > 0 ||
            result.CandidateReason == AkburaCandidateReason.Ambiguous)
        {
            return result;
        }

        return Next?.LookupSymbol(name, options, syntax, diagnostics) ??
               AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
    }

    public virtual ImmutableArray<AkburaSymbol> GetDeclaredSymbolsForScope(AkburaSyntax scopeDesignator)
    {
        return Next?.GetDeclaredSymbolsForScope(scopeDesignator) ??
               ImmutableArray<AkburaSymbol>.Empty;
    }

    public virtual ImmutableArray<Diagnostic> GetCSharpDiagnostics()
    {
        var diagnostics = new BindingDiagnosticBag();
        AddCSharpDiagnostics(diagnostics);
        return diagnostics.ToCSharpDiagnostics();
    }

    public virtual void AddCSharpDiagnostics(BindingDiagnosticBag diagnostics)
    {
        Next?.AddCSharpDiagnostics(diagnostics);
    }

    protected virtual AkburaSymbolInfo LookupSymbolInSingleBinder(
        string name,
        BinderLookupOptions options,
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
        return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
    }

    protected bool OwnsScope(AkburaSyntax scopeDesignator)
    {
        return ScopeDesignator != null &&
               (ReferenceEquals(ScopeDesignator, scopeDesignator) ||
                ReferenceEquals(ScopeDesignator.Green, scopeDesignator.Green));
    }

    protected ImmutableArray<AkburaSymbol> CreateSymbolsForDeclarations(
        ImmutableArray<AkburaDeclaration> declarations,
        params AkburaDeclarationKind[] allowedKinds)
    {
        if (declarations.IsDefaultOrEmpty)
        {
            return ImmutableArray<AkburaSymbol>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<AkburaSymbol>();
        foreach (var declaration in declarations)
        {
            if (allowedKinds.Length != 0 &&
                !allowedKinds.Contains(declaration.Kind))
            {
                continue;
            }

            if (TryCreateDeclaredSymbol(declaration, out var symbol))
            {
                builder.Add(symbol);
            }
        }

        return builder.ToImmutable();
    }

    protected bool TryCreateDeclaredSymbol(AkburaDeclaration declaration, out AkburaSymbol symbol)
    {
        var symbolInfo = SemanticModel.GetSymbolInfo(declaration.Syntax);
        if (symbolInfo.Symbol != null)
        {
            symbol = symbolInfo.Symbol;
            return true;
        }

        symbol = null!;
        return false;
    }

    protected static AkburaSymbol? FindDeclaredSymbol(
        ImmutableArray<AkburaSymbol> symbols,
        string name)
    {
        foreach (var symbol in symbols)
        {
            if (string.Equals(symbol.Name, name, System.StringComparison.Ordinal))
            {
                return symbol;
            }
        }

        return null;
    }
}
