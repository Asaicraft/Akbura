using System.Collections.Immutable;

namespace Akbura.Language;

internal readonly struct TailwindUtilityLookupCandidate
{
    public TailwindUtilityLookupCandidate(
        string name,
        ImmutableArray<TailwindUtilityParameterLookupCandidate> parameters,
        string targetTypeDisplay)
    {
        Name = name;
        Parameters = parameters.IsDefault
            ? ImmutableArray<TailwindUtilityParameterLookupCandidate>.Empty
            : parameters;
        TargetTypeDisplay = targetTypeDisplay ?? string.Empty;
    }

    public string Name { get; }

    public ImmutableArray<TailwindUtilityParameterLookupCandidate> Parameters { get; }

    public string TargetTypeDisplay { get; }
}

internal readonly struct TailwindUtilityParameterLookupCandidate
{
    public TailwindUtilityParameterLookupCandidate(
        string name,
        string typeDisplay,
        bool isOptional)
    {
        Name = name;
        TypeDisplay = typeDisplay;
        IsOptional = isOptional;
    }

    public string Name { get; }

    public string TypeDisplay { get; }

    public bool IsOptional { get; }
}
