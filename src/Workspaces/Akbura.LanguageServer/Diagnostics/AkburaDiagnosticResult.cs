using Microsoft.CodeAnalysis;

namespace Akbura.LanguageServer.Diagnostics;

internal sealed record AkburaDiagnosticResult(
    Uri Uri,
    int? LspVersion,
    VersionStamp DocumentVersion,
    VersionStamp ProjectVersion,
    string ResultId,
    SourceText Text,
    ImmutableArray<AkburaDiagnosticSpan> Diagnostics);
