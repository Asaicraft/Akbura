using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.Binder;

internal abstract class Binder
{
    private AkburaConversions? _lazyConversions;
    private OverloadResolver? _lazyOverloadResolution;

    protected Binder(
        AkburaSemanticModel semanticModel,
        Binder? next,
        Declaration? declaration,
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

    public Declaration? Declaration { get; }

    public AkburaSyntax? ScopeDesignator { get; }

    public AkburaBinderFlags Flags { get; }

    public AkburaConversions Conversions =>
        _lazyConversions ??= new AkburaConversions(this);

    public OverloadResolver OverloadResolution =>
        _lazyOverloadResolution ??= new OverloadResolver(this);

    public virtual string ScopeKey =>
        Declaration == null
            ? GetType().Name
            : $"{GetType().Name}:{Declaration.Kind}:{Declaration.Name}:{ScopeDesignator?.FullSpan.ToString() ?? DeclarationFacts.GetSyntax(Declaration).FullSpan.ToString()}";

    public virtual AkburaSymbolInfo LookupSymbol(AkburaSyntax syntax)
    {
        return Next?.LookupSymbol(syntax) ??
               AkburaSymbolInfo.None(Symbols.CandidateReason.UnsupportedSyntax);
    }

    public virtual Binder? GetBinder(AkburaSyntax syntax)
    {
        return Next?.GetBinder(syntax);
    }

    public virtual BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        return Next?.BindOperationSyntax(syntax) ??
               new BoundDeclaration(
                   syntax,
                   this,
                   AkburaSymbolInfo.None(Symbols.CandidateReason.UnsupportedSyntax));
    }

    public virtual BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        return Next?.BindSemanticSyntax(syntax) ??
               new BoundDeclaration(
                   syntax,
                   this,
                   AkburaSymbolInfo.None(Symbols.CandidateReason.UnsupportedSyntax));
    }

    public virtual AkburaSymbolInfo LookupSymbol(
        string name,
        BinderLookupOptions options,
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
        var result = LookupResult.GetInstance();
        LookupSymbolsInternal(
            result,
            name,
            arity: 0,
            options,
            originalBinder: this,
            syntax,
            diagnostics);

        return result.ToSymbolInfoAndFree(AkburaCandidateReason.NotFound);
    }

    public void LookupSymbolsInternal(
        LookupResult result,
        string name,
        int arity,
        BinderLookupOptions options,
        Binder originalBinder,
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (originalBinder == null)
        {
            throw new ArgumentNullException(nameof(originalBinder));
        }

        for (var binder = this; binder != null; binder = binder.Next)
        {
            binder.LookupSymbolsInSingleBinder(
                result,
                name,
                arity,
                options,
                originalBinder,
                syntax,
                diagnostics);

            if (result.IsComplete)
            {
                return;
            }
        }
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

    protected virtual void LookupSymbolsInSingleBinder(
        LookupResult result,
        string name,
        int arity,
        BinderLookupOptions options,
        Binder originalBinder,
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
    }

    protected bool OwnsScope(AkburaSyntax scopeDesignator)
    {
        return ScopeDesignator != null &&
               SemanticSyntaxIdentity.Equals(ScopeDesignator, scopeDesignator);
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
