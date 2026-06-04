using Akbura.Language.Syntax;

namespace Akbura.Language;

internal readonly struct BlendedNode
{
    public readonly AkburaSyntax? Node;
    public readonly SyntaxToken Token;
    public readonly Blender Blender;

    public BlendedNode(AkburaSyntax? node, SyntaxToken token, Blender blender)
    {
        Node = node;
        Token = token;
        Blender = blender;
    }
}
