namespace Akbura.Workspaces;

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
}
