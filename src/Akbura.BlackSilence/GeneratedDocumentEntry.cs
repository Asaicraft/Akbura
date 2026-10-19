using Microsoft.CodeAnalysis.Text;
using Akbura.Language.CodeGeneration;

namespace Akbura.BlackSilence;

/// <summary>
/// One completed immutable result. Only successful batches publish these entries.
/// </summary>
internal sealed class GeneratedDocumentEntry
{
    public GeneratedDocumentEntry(
        string identity,
        DocumentGenerationVersion version,
        string hintName,
        SourceText sourceText,
        DetachedCSharpDependencySet? semanticDependencies = null)
    {
        Identity = identity;
        Version = version;
        Source = new GeneratedSource(hintName, sourceText);
        SemanticDependencies = semanticDependencies;
    }

    public string Identity { get; }
    public DocumentGenerationVersion Version { get; }
    public GeneratedSource Source { get; }
    internal DetachedCSharpDependencySet? SemanticDependencies { get; }
}
