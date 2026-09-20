using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;

namespace Akbura.Language;

internal sealed partial class Parser
{
    // Only for markup-extension arguments/values/literals. Ordinary quoted
    // attributes and markup body text have different terminators.
    private bool TryReadReusableMarkupExtensionLiteralOwner<TNode>(out TNode node) where TNode : GreenNode
    {
        var previousBlender = _blender;
        var previousTrailingTrivia = _prevTokenTrailingTrivia;
        var previousPosition = _lexer.TextWindow.Position;

        if (!TryReadReusableIncrementalNode(out node))
        {
            return false;
        }

        // Nested extensions and C# expression arguments end with their own
        // explicit delimiter and keep the existing reuse contract.
        if (node.GetLastTerminal()?.Kind != SyntaxKind.AkTextLiteral)
        {
            return true;
        }

        // Text aggregates have no explicit closing token. In addition to the
        // blender's change-range checks, require the following NEW input to
        // still terminate the argument. A newly typed quote/name character
        // belongs to the literal, not to a new attribute after a missing '}'.
        // PeekIncrementalTokenKind does not populate the parser token buffer
        // here: successful node reuse requires that buffer to be exhausted.
        var nextKind = PeekIncrementalTokenKind();
        if (nextKind is SyntaxKind.CommaToken or
            SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken)
        {
            return true;
        }

        // Reject the tentative reuse without consuming any input. All three
        // owners (argument, value and literal) pass through this method.
        _blender = previousBlender;
        _prevTokenTrailingTrivia = previousTrailingTrivia;
        _lexer.TextWindow.Reset(previousPosition);
        node = null!;
        return false;
    }
}
