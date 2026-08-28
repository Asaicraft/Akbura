using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.References;

public readonly record struct AkburaDocumentHighlight(
    TextSpan Span,
    bool IsWrite);
