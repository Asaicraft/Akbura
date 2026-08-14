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
    PropertyValue,
    ApplyItem,
    AkcssModuleName,
    SelectorSnippet,
}
