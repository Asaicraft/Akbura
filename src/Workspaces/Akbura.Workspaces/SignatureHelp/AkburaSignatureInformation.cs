using System.Collections.Immutable;

namespace Akbura.Workspaces.SignatureHelp;

public sealed class AkburaSignatureInformation
{
    public AkburaSignatureInformation(
        string label,
        string? documentation,
        ImmutableArray<AkburaSignatureParameter> parameters)
    {
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Documentation = documentation;
        Parameters = parameters.IsDefault
            ? ImmutableArray<AkburaSignatureParameter>.Empty
            : parameters;
    }

    public string Label { get; }

    public string? Documentation { get; }

    public ImmutableArray<AkburaSignatureParameter> Parameters { get; }
}