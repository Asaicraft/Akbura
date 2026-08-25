namespace Akbura.Workspaces;

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
