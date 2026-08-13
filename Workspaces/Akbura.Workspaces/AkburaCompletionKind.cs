namespace Akbura.Workspaces;

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
    TailwindUtility,
    Keyword,
}
