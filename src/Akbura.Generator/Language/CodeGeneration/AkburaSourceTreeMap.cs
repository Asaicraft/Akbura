using Akbura.Language.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Immutable source ownership map. Construction never requests semantic models.
/// </summary>
internal sealed class AkburaSourceTreeMap
{
    private readonly Dictionary<AkburaSyntax, AkburaSyntaxTree> _syntaxTreesByRoot;

    public AkburaSourceTreeMap(
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

    public bool TryGetSyntaxTree(AkburaSyntax syntax, out AkburaSyntaxTree syntaxTree)
    {
        return _syntaxTreesByRoot.TryGetValue(syntax.Root, out syntaxTree!);
    }

    private void AddSyntaxTree(AkburaSyntaxTree syntaxTree)
    {
        var root = syntaxTree.GetRootSyntax();
        if (!_syntaxTreesByRoot.ContainsKey(root))
        {
            _syntaxTreesByRoot.Add(root, syntaxTree);
        }
    }
}
