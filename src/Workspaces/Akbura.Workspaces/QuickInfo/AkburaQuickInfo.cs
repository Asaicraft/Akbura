using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.QuickInfo;

/// <summary>
/// Describes native Quick Info for an Akbura source span.
/// </summary>
public sealed class AkburaQuickInfo
{
    internal AkburaQuickInfo(
        TextSpan sourceSpan,
        AkburaQuickInfoKind kind,
        string signature,
        ImmutableArray<string> details)
    {
        SourceSpan = sourceSpan;
        Kind = kind;
        Signature = signature ?? throw new ArgumentNullException(nameof(signature));
        Details = details.IsDefault
            ? ImmutableArray<string>.Empty
            : details;
    }

    public TextSpan SourceSpan { get; }

    public AkburaQuickInfoKind Kind { get; }

    public string Signature { get; }

    public ImmutableArray<string> Details { get; }
}
