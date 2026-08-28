using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;

namespace Akbura.Furioso;

internal sealed class AkcssGenerationSourceMap
{
    private readonly Dictionary<AkburaSyntax, AkburaSyntaxTree> _syntaxTreesByRoot = new();
    private readonly Dictionary<AkburaSyntax, IAkcssSymbol> _symbolsBySyntax = new();

    public AkcssGenerationSourceMap(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        foreach (var syntaxTree in syntaxTrees)
        {
            var root = syntaxTree.GetRootSyntax();
            if (!_syntaxTreesByRoot.ContainsKey(root))
            {
                _syntaxTreesByRoot.Add(root, syntaxTree);
            }
        }
    }

    public void RegisterModule(IAkcssModuleSymbol module)
    {
        foreach (var symbol in module.AkcssSymbols)
        {
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

    public bool TryGetLineDirective(
        AkburaSyntax syntax,
        out LinePositionSpan lineSpan,
        out string path)
    {
        if (!TryGetSourceSpan(syntax, out var sourceSpan, out path) ||
            !_syntaxTreesByRoot.TryGetValue(syntax.Root, out var syntaxTree))
        {
            lineSpan = default;
            return false;
        }

        if (string.IsNullOrWhiteSpace(path) ||
            path.IndexOf('"') >= 0 ||
            path.IndexOf('\r') >= 0 ||
            path.IndexOf('\n') >= 0 ||
            sourceSpan.Length == 0 ||
            (uint)sourceSpan.Start > (uint)syntaxTree.Text.Length ||
            (uint)sourceSpan.End > (uint)syntaxTree.Text.Length)
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

    public bool TryGetSourceSpan(
        AkburaSyntax syntax,
        out TextSpan span,
        out string path)
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
        return !string.IsNullOrWhiteSpace(path) &&
            span.Length > 0 &&
            (uint)span.Start <= (uint)syntaxTree.Text.Length &&
            (uint)span.End <= (uint)syntaxTree.Text.Length;
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
