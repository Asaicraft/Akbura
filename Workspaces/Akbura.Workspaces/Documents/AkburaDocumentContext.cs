namespace Akbura.Workspaces.Documents;

/// <summary>
/// Represents one document together with the exact solution and project
/// snapshots whose compilation contains the document syntax tree.
/// </summary>
public sealed class AkburaDocumentContext
{
    internal AkburaDocumentContext(
        AkburaProjectSnapshot project,
        AkburaDocumentSnapshot document)
        : this(
            AkburaSolutionSnapshot.Empty.WithProject(project),
            project,
            document)
    {
    }

    internal AkburaDocumentContext(
        AkburaSolutionSnapshot solution,
        AkburaProjectSnapshot project,
        AkburaDocumentSnapshot document)
    {
        Solution = solution ?? throw new ArgumentNullException(nameof(solution));
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Document = document ?? throw new ArgumentNullException(nameof(document));

        if (document.ProjectId != project.Id)
        {
            throw new ArgumentException(
                "The document belongs to another project.",
                nameof(document));
        }

        if (!solution.TryGetProject(project.Id, out var solutionProject) ||
            !ReferenceEquals(solutionProject, project))
        {
            throw new ArgumentException(
                "The project does not belong to the supplied solution snapshot.",
                nameof(project));
        }
    }

    public AkburaSolutionSnapshot Solution { get; }

    public AkburaProjectSnapshot Project { get; }

    public AkburaDocumentSnapshot Document { get; }
}