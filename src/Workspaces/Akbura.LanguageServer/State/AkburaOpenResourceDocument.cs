using Akbura.Workspaces.Resources;

namespace Akbura.LanguageServer.State;

internal sealed record AkburaOpenResourceDocument(
    Uri Uri,
    int Version,
    SourceText Text,
    ImmutableArray<AkburaOpenResourceDocumentProject> Projects);

internal readonly record struct AkburaOpenResourceDocumentProject(
    AkburaProjectId ProjectId,
    ResourceDocumentInput PersistedInput);
