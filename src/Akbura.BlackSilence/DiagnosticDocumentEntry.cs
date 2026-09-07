using Akbura.Diagnostics;
using System.Collections.Immutable;

namespace Akbura.BlackSilence;

internal sealed class DiagnosticDocumentEntry(
    DocumentDiagnosticVersion version,
    ImmutableArray<AkburaDiagnosticRecord> diagnostics)
{
    public DocumentDiagnosticVersion Version { get; } = version;
    public ImmutableArray<AkburaDiagnosticRecord> Diagnostics { get; } = diagnostics;
}
