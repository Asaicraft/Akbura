using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Concurrent;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Resolves canonical generation symbols only for modules reached by a writer.
/// Completed modules are published atomically; no lock is held during binding.
/// </summary>
internal sealed class AkcssGenerationSymbolResolver(
    AkburaSourceTreeMap sourceTreeMap,
    AkburaCompilation? compilation = null)
{
    private readonly ConcurrentDictionary<
        (AkburaSyntax Root, SyntaxKind Kind, TextSpan Span),
        IAkcssModuleSymbol> _modules = new();

    public void RegisterModule(IAkcssModuleSymbol module)
    {
        if (module.DeclaringSyntax is { } syntax)
        {
            _modules.TryAdd((syntax.Root, syntax.Kind, syntax.FullSpan), module);
        }
    }

    public IAkcssSymbol GetGenerationSymbol(IAkcssSymbol symbol)
    {
        if (symbol.DeclarationSyntax is not { } declarationSyntax)
        {
            return symbol;
        }

        var moduleSyntax = GetModuleSyntax(declarationSyntax);
        if (moduleSyntax == null || !TryResolveModule(moduleSyntax, out var module))
        {
            return symbol;
        }

        var symbols = module.AkcssSymbols;
        for (var i = 0; i < symbols.Length; i++)
        {
            var candidate = symbols[i];
            if (candidate.DeclarationSyntax is { } syntax &&
                (ReferenceEquals(syntax, declarationSyntax) ||
                 ReferenceEquals(syntax.Root, declarationSyntax.Root) &&
                 syntax.Kind == declarationSyntax.Kind && syntax.FullSpan == declarationSyntax.FullSpan))
            {
                return candidate;
            }
        }

        return symbol;
    }

    private bool TryResolveModule(AkburaSyntax moduleSyntax, out IAkcssModuleSymbol module)
    {
        var key = (moduleSyntax.Root, moduleSyntax.Kind, moduleSyntax.FullSpan);
        if (_modules.TryGetValue(key, out module!))
        {
            return true;
        }

        if (compilation == null || !sourceTreeMap.TryGetSyntaxTree(moduleSyntax, out var syntaxTree))
        {
            module = null!;
            return false;
        }

#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.SemanticBinding);
#endif
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        if (semanticModel.GetDeclaredSymbol(moduleSyntax) is not IAkcssModuleSymbol resolved)
        {
            module = null!;
            return false;
        }

        module = _modules.GetOrAdd(key, resolved);
        return true;
    }

    private static AkburaSyntax? GetModuleSyntax(AkburaSyntax syntax)
    {
        for (var current = syntax; current != null; current = current.Parent)
        {
            if (current is AkcssDocumentSyntax or InlineAkcssBlockSyntax)
            {
                return current;
            }
        }

        return null;
    }
}
