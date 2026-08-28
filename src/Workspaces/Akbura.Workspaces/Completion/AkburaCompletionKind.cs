namespace Akbura.Workspaces.Completion;

/// <summary>
/// Identifies the language construct represented by a completion item.
/// </summary>
public enum AkburaCompletionKind
{
    Component,
    ClosingTag,
    PropertyElement,
    Parameter,
    Property,
    Event,
    Command,
    MarkupExtension,
    AkcssStyle,
    AkcssModule,
    AkcssValue,
    AkcssColor,
    TailwindUtility,
    Keyword,
    Hook,
}
