// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

namespace Akbura.Language;

/// <summary>
/// Represents no location at all.
/// </summary>
internal sealed class NoLocation : Location
{
    public static readonly Location Singleton = new NoLocation();

    private NoLocation()
    {
    }

    public override LocationKind Kind
    {
        get
        {
            return LocationKind.None;
        }
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj);
    }

    public override int GetHashCode()
    {
        return 0x16487756;
    }
}
