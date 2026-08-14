using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces;

internal enum AkburaCSharpProjectionMappingKind
{
    ActiveFragment,
    UsingDirective,
}

internal readonly struct AkburaCSharpProjectionMapping
{
    public AkburaCSharpProjectionMapping(
        AkburaCSharpProjectionMappingKind kind,
        TextSpan hostSpan,
        TextSpan projectedSpan)
    {
        if (hostSpan.Length != projectedSpan.Length)
        {
            throw new ArgumentException(
                "Mapped host and projected regions must have equal lengths.",
                nameof(projectedSpan));
        }

        Kind = kind;
        HostSpan = hostSpan;
        ProjectedSpan = projectedSpan;
    }

    public AkburaCSharpProjectionMappingKind Kind { get; }

    public TextSpan HostSpan { get; }

    public TextSpan ProjectedSpan { get; }
}
