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

    ClassName = 20,
    StructName = 21,
    InterfaceName = 22,
    EnumName = 23,
    DelegateName = 24,
    TypeParameterName = 25,

    MethodName = 26,
    ExtensionMethodName = 27,
    PropertyName = 28,
    EventName = 29,
    FieldName = 30,
    EnumMemberName = 31,
    ConstantName = 32,
    LocalName = 33,
    ParameterName = 34,
    LabelName = 35,

    LastKind = LabelName,
}