using Akbura.Workspaces.Resources;

namespace Akbura.VisualStudio;

internal readonly struct AkburaResourceDocumentRegistration
{
    public AkburaResourceDocumentRegistration(AkburaProjectId projectId, ResourceDocumentInput input)
    {
        ProjectId = projectId;
        Input = input;
    }

    public AkburaProjectId ProjectId { get; }

    public ResourceDocumentInput Input { get; }
}
