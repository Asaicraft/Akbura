using Akbura.Language;
using Akbura.Language.CodeGeneration;
using System.Collections.Immutable;

namespace Akbura.BlackSilence;

internal sealed class BlackSilenceProjectSnapshot
{
    public BlackSilenceProjectSnapshot(
        long version,
        GeneratorProjectOptions options,
        AkburaProjectIndex index,
        ImmutableArray<DocumentSyntaxVersion> documents,
        object declarationEnvironment,
        ImmutableDictionary<string, GeneratedDocumentEntry> entries,
        ImmutableDictionary<AkburaSyntaxTree, SemanticModelState> semanticStates,
        ImmutableDictionary<string, DiagnosticDocumentEntry>? diagnosticEntries = null)
    {
        Version = version;
        Options = options;
        Index = index;
        Documents = documents;
        DeclarationEnvironment = declarationEnvironment;
        Entries = entries;
        SemanticStates = semanticStates;
        DiagnosticEntries = diagnosticEntries ?? ImmutableDictionary<string, DiagnosticDocumentEntry>.Empty;
    }

    public long Version { get; }
    public GeneratorProjectOptions Options { get; }
    public AkburaProjectIndex Index { get; }
    public ImmutableArray<DocumentSyntaxVersion> Documents { get; }
    public object DeclarationEnvironment { get; }
    public ImmutableDictionary<string, GeneratedDocumentEntry> Entries { get; }
    public ImmutableDictionary<AkburaSyntaxTree, SemanticModelState> SemanticStates { get; }
    public ImmutableDictionary<string, DiagnosticDocumentEntry> DiagnosticEntries { get; }
}
