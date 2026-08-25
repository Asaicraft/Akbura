using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.AutomaticPairing;

internal readonly struct AkburaPairContext
{
    public AkburaPairContext(
        AkburaPairContextKind kind,
        int position,
        bool isAkcss = false,
        AkburaCSharpCompletionContextKind csharpKind = default,
        TextSpan hostSpan = default)
    {
        Kind = kind;
        Position = position;
        IsAkcss = isAkcss;
        CSharpKind = csharpKind;
        HostSpan = hostSpan;
    }

    public AkburaPairContextKind Kind { get; }

    public int Position { get; }

    public bool IsAkcss { get; }

    public AkburaCSharpCompletionContextKind CSharpKind { get; }

    public TextSpan HostSpan { get; }

    public bool IsDefault => Kind == AkburaPairContextKind.None;
}
