using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Maps AKCSS symbols and operations back to their original source documents.
/// </summary>
internal sealed class AkcssGenerationSourceMap
{
    private readonly Dictionary<AkburaSyntax, AkburaSyntaxTree> _syntaxTreesByRoot;
    private readonly Dictionary<AkburaSyntax, IAkcssSymbol> _symbolsBySyntax = [];

    public AkcssGenerationSourceMap(
        ImmutableArray<AkburaSyntaxTree> componentSyntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees)
    {
        _syntaxTreesByRoot = new Dictionary<AkburaSyntax, AkburaSyntaxTree>(
            componentSyntaxTrees.Length + akcssSyntaxTrees.Length);

        for (var i = 0; i < componentSyntaxTrees.Length; i++)
        {
            AddSyntaxTree(componentSyntaxTrees[i]);
        }

        for (var i = 0; i < akcssSyntaxTrees.Length; i++)
        {
            AddSyntaxTree(akcssSyntaxTrees[i]);
        }
    }

    public void RegisterModule(IAkcssModuleSymbol module)
    {
        var symbols = module.AkcssSymbols;

        for (var i = 0; i < symbols.Length; i++)
        {
            var symbol = symbols[i];

            if (symbol.DeclarationSyntax is { } syntax)
            {
                _symbolsBySyntax[syntax] = symbol;
            }
        }
    }

    public IAkcssSymbol GetGenerationSymbol(IAkcssSymbol symbol)
    {
        if (symbol.DeclarationSyntax is not { } declarationSyntax)
        {
            return symbol;
        }

        if (_symbolsBySyntax.TryGetValue(declarationSyntax, out var registered))
        {
            return registered;
        }

        foreach (var pair in _symbolsBySyntax)
        {
            if (ReferenceEquals(pair.Key.Root, declarationSyntax.Root) &&
                pair.Key.Kind == declarationSyntax.Kind &&
                pair.Key.FullSpan == declarationSyntax.FullSpan)
            {
                return pair.Value;
            }
        }

        return symbol;
    }

    public bool TryGetLineDirective(AkburaSyntax syntax, out LinePositionSpan lineSpan, out string path)
    {
        if (!TryGetSourceSpan(syntax, out var sourceSpan, out path) ||
            !_syntaxTreesByRoot.TryGetValue(syntax.Root, out var syntaxTree))
        {
            lineSpan = default;
            path = string.Empty;
            return false;
        }

        lineSpan = syntaxTree.Text.Lines.GetLinePositionSpan(sourceSpan);

        if (!IsValidLineSpan(lineSpan))
        {
            lineSpan = default;
            path = string.Empty;
            return false;
        }

        return true;
    }

    public bool TryGetSourceSpan(AkburaSyntax syntax, out TextSpan span, out string path)
    {
        if (!_syntaxTreesByRoot.TryGetValue(syntax.Root, out var syntaxTree))
        {
            span = default;
            path = string.Empty;
            return false;
        }

        path = syntaxTree switch
        {
            AkcssSyntaxTree { FilePath.Length: 0 } akcssTree => akcssTree.LogicalName,
            _ => syntaxTree.FilePath,
        };

        span = syntax.Span;

        if (string.IsNullOrWhiteSpace(path) ||
            path.IndexOf('"') >= 0 ||
            path.IndexOf('\r') >= 0 ||
            path.IndexOf('\n') >= 0 ||
            span.Length == 0 ||
            (uint)span.Start > (uint)syntaxTree.Text.Length ||
            (uint)span.End > (uint)syntaxTree.Text.Length)
        {
            span = default;
            path = string.Empty;
            return false;
        }

        return true;
    }

    private void AddSyntaxTree(AkburaSyntaxTree syntaxTree)
    {
        var root = syntaxTree.GetRootSyntax();

        if (!_syntaxTreesByRoot.ContainsKey(root))
        {
            _syntaxTreesByRoot.Add(root, syntaxTree);
        }
    }

    private static bool IsValidLineSpan(LinePositionSpan lineSpan)
    {
        return IsValidLinePosition(lineSpan.Start) &&
            IsValidLinePosition(lineSpan.End) &&
            (lineSpan.End.Line > lineSpan.Start.Line ||
             lineSpan.End.Character > lineSpan.Start.Character);
    }

    private static bool IsValidLinePosition(LinePosition position)
    {
        return (uint)position.Line < 0x20000000 &&
            position.Line != 0xfeefee &&
            (uint)position.Character < 0x10000;
    }
}
