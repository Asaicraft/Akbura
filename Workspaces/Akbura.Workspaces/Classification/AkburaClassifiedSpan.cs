using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Classification;

public readonly record struct AkburaClassifiedSpan(
    TextSpan Span,
    AkburaClassificationKind Kind);
