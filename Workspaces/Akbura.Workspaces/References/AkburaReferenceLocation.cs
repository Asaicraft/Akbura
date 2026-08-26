using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.References;

public readonly record struct AkburaReferenceLocation(
    Uri Uri,
    TextSpan Span,
    bool IsDeclaration,
    bool IsWrite);
