namespace Akbura.Workspaces.Documents;

/// <summary>
/// Opaque version identities for a captured solution, project, and document.
/// </summary>
public sealed record AkburaDocumentSnapshotIdentity(
    string Solution,
    string Project,
    string Document)
{
    internal static AkburaDocumentSnapshotIdentity Capture(AkburaDocumentContext context) =>
        new(context.Solution.Version.ToString(), context.Project.Version.ToString(), context.Document.Version.ToString());
}
