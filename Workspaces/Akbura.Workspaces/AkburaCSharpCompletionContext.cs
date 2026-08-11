using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces;

/// <summary>
/// Describes a C# fragment at a completion position in an Akbura document.
/// </summary>
public readonly struct AkburaCSharpCompletionContext
{
    internal AkburaCSharpCompletionContext(
        AkburaCSharpCompletionContextKind kind,
        TextSpan ownerSpan,
        TextSpan hostSpan,
        int hostPosition)
    {
        Kind = kind;
        OwnerSpan = ownerSpan;
        HostSpan = hostSpan;
        HostPosition = hostPosition;
    }

    /// <summary>
    /// Gets the kind of embedded C# fragment.
    /// </summary>
    public AkburaCSharpCompletionContextKind Kind { get; }

    /// <summary>
    /// Gets the source span occupied by the embedded C# fragment.
    /// </summary>
    public TextSpan HostSpan { get; }

    /// <summary>
    /// Gets the completion position in the Akbura document.
    /// </summary>
    public int HostPosition { get; }

    /// <summary>
    /// Gets the completion position relative to <see cref="HostSpan"/>.
    /// </summary>
    public int RelativePosition => HostPosition - HostSpan.Start;

    internal TextSpan OwnerSpan { get; }
}

/// <summary>
/// Identifies the kind of C# fragment used for completion.
/// </summary>
public enum AkburaCSharpCompletionContextKind
{
    /// <summary>
    /// No C# completion context was found.
    /// </summary>
    None = 0,

    /// <summary>
    /// A C# expression enclosed by Akbura braces.
    /// </summary>
    Expression,
}
