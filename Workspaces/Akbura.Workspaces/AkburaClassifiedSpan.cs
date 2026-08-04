using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces;

public readonly record struct AkburaClassifiedSpan(
    TextSpan Span,
    AkburaClassificationKind Kind);
