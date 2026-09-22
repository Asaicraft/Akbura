using System.Collections.Immutable;
using Akbura.Workspaces.Resources;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Contains one evaluated Roslyn project and all Akbura source documents
/// discovered for it.
/// </summary>
public sealed class AkburaLoadedProject
{
    public AkburaLoadedProject(ProjectContext context, ImmutableArray<AkburaDocumentInput> documents, ImmutableArray<AkburaProjectLoadDiagnostic> diagnostics) : this(context, documents, ImmutableArray<ResourceDocumentInput>.Empty, diagnostics)
    {
    }

    internal AkburaLoadedProject(ProjectContext context, ImmutableArray<AkburaDocumentInput> documents, ImmutableArray<ResourceDocumentInput> resourceDocuments, ImmutableArray<AkburaProjectLoadDiagnostic> diagnostics)
    {
        Context = context ??
            throw new ArgumentNullException(nameof(context));
        Documents = documents.IsDefault
            ? ImmutableArray<AkburaDocumentInput>.Empty
            : documents;
        ResourceDocuments = resourceDocuments.IsDefault
            ? ImmutableArray<ResourceDocumentInput>.Empty
            : resourceDocuments;
        Diagnostics = diagnostics.IsDefault
            ? ImmutableArray<AkburaProjectLoadDiagnostic>.Empty
            : diagnostics;
    }

    public ProjectContext Context { get; }

    public ImmutableArray<AkburaDocumentInput> Documents { get; }

    internal ImmutableArray<ResourceDocumentInput> ResourceDocuments { get; }

    public ImmutableArray<AkburaProjectLoadDiagnostic> Diagnostics { get; }
}
