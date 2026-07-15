// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace Akbura.Language;

/// <summary>
/// A program location in metadata.
/// </summary>
internal sealed class MetadataLocation : Location, IEquatable<MetadataLocation?>
{
    private readonly AkburaReferencedModule _module;

    internal MetadataLocation(AkburaReferencedModule module)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
    }

    public override LocationKind Kind
    {
        get
        {
            return LocationKind.MetadataFile;
        }
    }

    internal override AkburaReferencedModule MetadataModule
    {
        get
        {
            return _module;
        }
    }

    public bool Equals(MetadataLocation? other)
    {
        return other != null && ReferenceEquals(other._module, _module);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MetadataLocation);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(_module);
    }
}
