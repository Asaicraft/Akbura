namespace Akbura.Workspaces;

/// <summary>
/// Editor-independent classification categories.
/// Visual Studio, LSP and Rider map these values to their own protocols.
/// </summary>
public enum AkburaClassificationKind
{
    Keyword,
    Namespace,
    Type,
    Component,
    Attribute,
    Identifier,
    Directive,
    String,
    Number,
    Comment,
    Operator,
    Punctuation,
    MarkupText,
    EmbeddedCSharp,
}
