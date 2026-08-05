namespace Akbura.Workspaces;

/// <summary>
/// Editor-independent classification categories.
/// Visual Studio, LSP and Rider map these values to their own protocols.
/// </summary>
public enum AkburaClassificationKind
{
    Keyword = 0,
    Namespace = 1,
    Type = 2,
    Component = 3,
    Attribute = 4,
    Identifier = 5,
    Directive = 6,
    String = 7,
    Number = 8,
    Comment = 9,
    Operator = 10,
    Punctuation = 11,
    MarkupText = 12,

    Utility = 13,
    UtilityModifier = 14,

    MarkupExtensionType = 15,
    MarkupExtensionProperty = 16,
    MarkupExtensionValue = 17,
    MarkupExtensionPunctuation = 18,

    EmbeddedCSharp = 19,

    LastKind = EmbeddedCSharp,
}