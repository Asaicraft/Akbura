using System.Collections.Immutable;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Contains one evaluated Roslyn project and all Akbura source documents
/// discovered for it.
/// </summary>
public sealed class AkburaLoadedProject
{
    public AkburaLoadedProject(
        ProjectContext context,
        ImmutableArray<AkburaDocumentInput> documents,
        ImmutableArray<AkburaProjectLoadDiagnostic> diagnostics)
    {
        Context = context ??
            throw new ArgumentNullException(nameof(context));
        Documents = documents.IsDefault
            ? ImmutableArray<AkburaDocumentInput>.Empty
            : documents;
        Diagnostics = diagnostics.IsDefault
            ? ImmutableArray<AkburaProjectLoadDiagnostic>.Empty
            : diagnostics;
    }

    public ProjectContext Context { get; }

    public ImmutableArray<AkburaDocumentInput> Documents { get; }

    public ImmutableArray<AkburaProjectLoadDiagnostic> Diagnostics { get; }
}