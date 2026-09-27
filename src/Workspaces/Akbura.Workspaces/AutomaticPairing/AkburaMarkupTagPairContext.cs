using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.AutomaticPairing;

public readonly record struct AkburaMarkupTagPairContext(
    string ParentElementName,
    TextSpan ParentEndTagSpan);
