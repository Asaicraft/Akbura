using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Maps AKCSS symbols and operations back to their original source documents.
/// </summary>
internal sealed class AkcssGenerationSourceMap
{
    private readonly AkburaSourceTreeMap _sourceTreeMap;
    private readonly AkcssGenerationSymbolResolver _symbolResolver;

    public AkcssGenerationSourceMap(
        ImmutableArray<AkburaSyntaxTree> componentSyntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees)
    {
        _sourceTreeMap = new AkburaSourceTreeMap(componentSyntaxTrees, akcssSyntaxTrees);
        _symbolResolver = new AkcssGenerationSymbolResolver(_sourceTreeMap);
    }

    public AkcssGenerationSourceMap(
        AkburaSourceTreeMap sourceTreeMap,
        AkcssGenerationSymbolResolver symbolResolver)
    {
        _sourceTreeMap = sourceTreeMap;
        _symbolResolver = symbolResolver;
    }

    public void RegisterModule(IAkcssModuleSymbol module)
    {
        _symbolResolver.RegisterModule(module);
    }

    public IAkcssSymbol GetGenerationSymbol(IAkcssSymbol symbol)
    {
        return _symbolResolver.GetGenerationSymbol(symbol);
    }

    public bool TryGetLineDirective(AkburaSyntax syntax, out LinePositionSpan lineSpan, out string path)
    {
        if (!TryGetSourceSpan(syntax, out var sourceSpan, out path) ||
            !_sourceTreeMap.TryGetSyntaxTree(syntax, out var syntaxTree))
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
        if (!_sourceTreeMap.TryGetSyntaxTree(syntax, out var syntaxTree))
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
