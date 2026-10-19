using Akbura.Diagnostics;
using Akbura.Language.CodeGeneration;
using System.Collections.Immutable;

namespace Akbura.BlackSilence;

internal sealed class DiagnosticDocumentEntry(
    DocumentDiagnosticVersion version,
    ImmutableArray<AkburaDiagnosticRecord> diagnostics,
    DetachedCSharpDependencySet? semanticDependencies = null)
{
    public DocumentDiagnosticVersion Version { get; } = version;
    public ImmutableArray<AkburaDiagnosticRecord> Diagnostics { get; } = diagnostics;
    internal DetachedCSharpDependencySet? SemanticDependencies { get; } = semanticDependencies;
}
