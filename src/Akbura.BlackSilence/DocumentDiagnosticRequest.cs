using Akbura.Language.CodeGeneration;

namespace Akbura.BlackSilence;

internal readonly record struct DocumentDiagnosticRequest(
    DocumentSyntaxVersion Document,
    DocumentDiagnosticVersion Version,
    DiagnosticDocumentEntry? Previous);
