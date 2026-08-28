using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.SignatureHelp;

public sealed class AkburaSignatureHelp
{
    public AkburaSignatureHelp(
        TextSpan applicableSpan,
        ImmutableArray<AkburaSignatureInformation> signatures,
        int activeSignature,
        int activeParameter)
    {
        ApplicableSpan = applicableSpan;
        Signatures = signatures.IsDefault
            ? ImmutableArray<AkburaSignatureInformation>.Empty
            : signatures;
        ActiveSignature = activeSignature;
        ActiveParameter = activeParameter;
    }

    public TextSpan ApplicableSpan { get; }

    public ImmutableArray<AkburaSignatureInformation> Signatures { get; }

    public int ActiveSignature { get; }

    public int ActiveParameter { get; }
}