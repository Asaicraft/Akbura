namespace Akbura.Workspaces.AutomaticPairing;

internal enum AkburaPairContextKind
{
    None,
    MarkupText,
    MarkupStartTag,
    MarkupLiteralAttributeValue,
    MarkupExtension,
    AkcssSyntax,
    EmbeddedCSharp,
    CSharpStringText,
    CSharpInterpolation,
    Comment,
}
