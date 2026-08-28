using Akbura.Language.Operations;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using System.Collections.Immutable;

namespace Akbura.Language;

internal readonly struct MarkupExtensionBindingResult
{
    public MarkupExtensionBindingResult(
        MarkupExtensionValue? value,
        CSharpSymbolDefinition resultType,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        AkburaConversion conversion = default)
    {
        Value = value;
        ResultType = resultType;
        Diagnostics = diagnostics.IsDefault
            ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
            : diagnostics;
        Conversion = conversion;
    }

    public MarkupExtensionValue? Value { get; }

    public CSharpSymbolDefinition ResultType { get; }

    public ImmutableArray<AkburaSemanticDiagnostic> Diagnostics { get; }

    public AkburaConversion Conversion { get; }
}
