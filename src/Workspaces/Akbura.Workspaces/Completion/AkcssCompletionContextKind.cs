namespace Akbura.Workspaces.Completion;

/// <summary>
/// Identifies the AKCSS construct being completed.
/// </summary>
public enum AkcssCompletionContextKind
{
    None = 0,
    TopLevel,
    BodyMember,
    PropertyName,
    AttachedPropertyExpression,
    PropertyValue,
    ApplyItem,
    AkcssModuleName,
    SelectorSnippet,
    IncludeName,
    BaseTheme,
    TemplateBindingProperty,
    TemplatePart,
    PseudoClass,
    TemplateTraversal,
    TemplateMarkupBody,
    IncludeArgument,
}
