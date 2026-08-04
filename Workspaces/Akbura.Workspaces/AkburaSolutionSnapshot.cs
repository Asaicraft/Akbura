using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

/// <summary>
/// Immutable collection of Akbura project snapshots.
/// </summary>
public sealed class AkburaSolutionSnapshot
{
    private AkburaSolutionSnapshot(
        VersionStamp version,
        ImmutableDictionary<AkburaProjectId, AkburaProjectSnapshot> projects)
    {
        Version = version;
        Projects = projects;
    }

    public VersionStamp Version { get; }

    public ImmutableDictionary<
        AkburaProjectId,
        AkburaProjectSnapshot> Projects { get; }

    public static AkburaSolutionSnapshot Empty { get; } =
        new AkburaSolutionSnapshot(
            VersionStamp.Create(),
            ImmutableDictionary<
                AkburaProjectId,
                AkburaProjectSnapshot>.Empty);

    public bool TryGetProject(
        AkburaProjectId projectId,
        out AkburaProjectSnapshot project)
    {
        return Projects.TryGetValue(projectId, out project!);
    }

    public AkburaProjectSnapshot GetRequiredProject(
        AkburaProjectId projectId)
    {
        if (!TryGetProject(projectId, out var project))
        {
            throw new KeyNotFoundException(
                $"Project '{projectId}' was not found.");
        }

        return project;
    }

    public bool TryGetDocument(
        AkburaDocumentId documentId,
        out AkburaDocumentSnapshot document)
    {
        foreach (var project in Projects.Values)
        {
            if (project.TryGetDocument(documentId, out document))
            {
                return true;
            }
        }

        document = null!;
        return false;
    }

    public bool TryGetDocument(
        Uri uri,
        out AkburaDocumentSnapshot document)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        foreach (var project in Projects.Values)
        {
            if (project.TryGetDocument(uri, out document))
            {
                return true;
            }
        }

        document = null!;
        return false;
    }

    public AkburaDocumentSnapshot GetRequiredDocument(
        AkburaDocumentId documentId)
    {
        if (!TryGetDocument(documentId, out var document))
        {
            throw new KeyNotFoundException(
                $"Document '{documentId}' was not found.");
        }

        return document;
    }

    internal AkburaSolutionSnapshot WithProject(
        AkburaProjectSnapshot project)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        return new AkburaSolutionSnapshot(
            VersionStamp.Create(),
            Projects.SetItem(project.Id, project));
    }

    internal AkburaSolutionSnapshot RemoveProject(
        AkburaProjectId projectId)
    {
        if (!Projects.ContainsKey(projectId))
        {
            return this;
        }

        return new AkburaSolutionSnapshot(
            VersionStamp.Create(),
            Projects.Remove(projectId));
    }
}
