using System.Collections.Immutable;

namespace Akbura.Workspaces.References;

public sealed class AkburaReferenceResult
{
    internal AkburaReferenceResult(
        AkburaSymbolKey? symbol,
        string? name,
        ImmutableArray<AkburaReferenceLocation> locations)
    {
        Symbol = symbol;
        Name = name;
        Locations = locations.IsDefault
            ? ImmutableArray<AkburaReferenceLocation>.Empty
            : locations;
    }

    public AkburaSymbolKey? Symbol { get; }

    public string? Name { get; }

    public ImmutableArray<AkburaReferenceLocation> Locations { get; }

    public bool IsEmpty => Symbol == null;
}
