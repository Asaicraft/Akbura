namespace Akbura.Workspaces;

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
}
