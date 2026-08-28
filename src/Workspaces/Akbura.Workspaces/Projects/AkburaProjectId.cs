using Microsoft.CodeAnalysis;

namespace Akbura.Workspaces.Projects;

public readonly record struct AkburaProjectId(Guid Value)
{
    public static AkburaProjectId CreateNew()
    {
        return new AkburaProjectId(Guid.NewGuid());
    }

    internal static AkburaProjectId FromRoslyn(ProjectId projectId)
    {
        if (projectId == null)
        {
            throw new ArgumentNullException(nameof(projectId));
        }
        return new AkburaProjectId(projectId.Id);
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
