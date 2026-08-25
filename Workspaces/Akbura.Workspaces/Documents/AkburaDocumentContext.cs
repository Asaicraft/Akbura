namespace Akbura.Workspaces.Documents;

/// <summary>
/// Represents one document together with the exact project snapshot
/// whose compilation contains the document syntax tree.
/// </summary>
public sealed class AkburaDocumentContext
{
    internal AkburaDocumentContext(
        AkburaProjectSnapshot project,
        AkburaDocumentSnapshot document)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));

        Document = document ?? throw new ArgumentNullException(nameof(document));

        if (document.ProjectId != project.Id)
        {
            throw new ArgumentException(
                "The document belongs to another project.",
                nameof(document));
        }
    }

    public AkburaProjectSnapshot Project { get; }

    public AkburaDocumentSnapshot Document { get; }
}