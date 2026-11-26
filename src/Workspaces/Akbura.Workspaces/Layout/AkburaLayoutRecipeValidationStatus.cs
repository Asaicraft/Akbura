namespace Akbura.Workspaces.Layout;

/// <summary>
/// Describes the outcome of an isolated semantic analysis of a reference example.
/// </summary>
public enum AkburaLayoutRecipeValidationStatus
{
    NotRun,
    Passed,
    Failed,
    Incomplete,
}
