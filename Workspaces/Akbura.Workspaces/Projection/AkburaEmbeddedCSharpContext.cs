using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Projection;

internal readonly struct AkburaEmbeddedCSharpContext
{
    public AkburaEmbeddedCSharpContext(
        AkburaCSharpCompletionContextKind kind,
        SyntaxKind ownerKind,
        TextSpan ownerSpan,
        TextSpan hostSpan,
        int hostPosition)
    {
        Kind = kind;
        OwnerKind = ownerKind;
        OwnerSpan = ownerSpan;
        HostSpan = hostSpan;
        HostPosition = hostPosition;
    }

    public AkburaCSharpCompletionContextKind Kind { get; }

    public SyntaxKind OwnerKind { get; }

    public TextSpan OwnerSpan { get; }

    public TextSpan HostSpan { get; }

    public int HostPosition { get; }

    public int RelativePosition => HostPosition - HostSpan.Start;

    public AkburaCSharpCompletionContext ToCompletionContext()
    {
        return new AkburaCSharpCompletionContext(
            Kind,
            OwnerKind,
            OwnerSpan,
            HostSpan,
            HostPosition);
    }
}
