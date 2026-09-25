using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Completion;

internal enum AkburaMarkupAttributeMode
{
    None = 0,
    Bind,
    Out,
}

internal readonly record struct AkburaMarkupAttributeIdentity(
    string Name,
    AkburaMarkupAttributeMode Mode,
    TextSpan FullNameSpan);
