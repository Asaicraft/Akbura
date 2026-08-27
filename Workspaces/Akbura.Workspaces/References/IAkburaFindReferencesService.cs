namespace Akbura.Workspaces.References;

public interface IAkburaFindReferencesService
{
    AkburaReferenceResult FindReferences(
        AkburaDocumentContext context,
        int position,
        bool includeDeclaration,
        CancellationToken cancellationToken = default);
}
