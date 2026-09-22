namespace Akbura.Workspaces.Completion;

/// <summary>
/// Identifies the syntactic construct being completed.
/// </summary>
public enum AkburaCompletionContextKind
{
    None = 0,
    ComponentName = 1,
    ClosingComponentName = 2,
    AttributeName = 3,
    PropertyElementName = 4,
    MarkupExtensionType = 5,
    TopLevel = 6,
    DeclarationModifier = 7,
    AttributeValue = 8,
    MarkupStatement = 9,
    MarkupConditionalContinuation = 10,
    MarkupExtensionArgumentName = 11,
    MarkupExtensionArgumentValue = 12,
    BindingPath = 13,
}
